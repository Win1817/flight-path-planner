// Ported from src/components/FlightMap.tsx — keep in sync with that file's map setup.
// The host (C#) drives this page with JSON messages (window.lunaHost for CEF, the WebView2 message channel on Windows):
//   {kind:'begin', version, viewportMode, fit, fitBounds, shown, inView, total, anyHighlight, needsViewport}
//   {kind:'chunk', version, features:[...]}   (zero or more)
//   {kind:'end',   version}                     -> the assembled features replace the map data in one step
//   {kind:'highlights', ids:[...]}              -> hovered/active ids
// and receives:
//   {kind:'ready'} | {kind:'click'|'hover', id, dataType} | {kind:'viewport', west, south, east, north, zoom}

function postToHost(obj) {
  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.postMessage(JSON.stringify(obj));
  } else if (window.csharpBridge && obj.kind === 'viewport') {
    window.csharpBridge.OnViewport(obj.west, obj.south, obj.east, obj.north);
  }
}

function bridgeCall(kind, id, dataType) {
  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.postMessage(JSON.stringify({ kind: kind, id: id, dataType: dataType }));
  } else if (window.csharpBridge) {
    if (kind === 'click') window.csharpBridge.OnZoneClick(id, dataType);
    else window.csharpBridge.OnZoneHover(id || '');
  }
}

function escapeHtml(value) {
  return String(value)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

function formatArea(squareMeters) {
  if (squareMeters >= 1000000) return (squareMeters / 1000000).toFixed(2) + ' km²';
  if (squareMeters >= 10000) return (squareMeters / 10000).toFixed(2) + ' ha';
  return squareMeters.toFixed(0) + ' m²';
}

function getBoundsFromGeoJSON(geojson) {
  if (!geojson.features || geojson.features.length === 0) return null;
  let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;

  function visit(coords) {
    if (typeof coords[0] === 'number') {
      const [x, y] = coords;
      if (x < minX) minX = x;
      if (y < minY) minY = y;
      if (x > maxX) maxX = x;
      if (y > maxY) maxY = y;
    } else {
      coords.forEach(visit);
    }
  }

  geojson.features.forEach(f => visit(f.geometry.coordinates));
  if (!isFinite(minX)) return null;
  return [[minX, minY], [maxX, maxY]];
}

// Dark basemap (Esri World Dark Gray, free with attribution). CARTO's dark tiles now require an API key;
// swap these constants to change the basemap.
const BASEMAP_TILES = 'https://server.arcgisonline.com/ArcGIS/rest/services/Canvas/World_Dark_Gray_Base/MapServer/tile/{z}/{y}/{x}';
const BASEMAP_LABEL_TILES = 'https://server.arcgisonline.com/ArcGIS/rest/services/Canvas/World_Dark_Gray_Reference/MapServer/tile/{z}/{y}/{x}';
const BASEMAP_ATTRIBUTION = 'Tiles © Esri — Esri, HERE, Garmin, OpenStreetMap contributors';

let map = null;
let popup = null;
let mapLoaded = false;
let hoverIds = [];              // hovered/active item ids sent separately from the data (at most a couple)
let anyFeatureHighlight = false; // true when the current data carries "hl":1 flags (selection, report matches...)
let viewportMode = false;        // large dataset: the host sends only what's in view, and we tell it where we are
let currentVersion = 0;          // newest data update; older chunks are ignored
let latestBegin = null;
let featureBuffer = [];
let queuedMessages = [];         // data messages that arrived before the map finished loading
let viewportTimer = null;

function initMap() {
  map = new maplibregl.Map({
    container: 'map',
    style: {
      version: 8,
      sources: {
        'basemap': {
          type: 'raster',
          tiles: [BASEMAP_TILES],
          tileSize: 256,
          maxzoom: 16,
          attribution: BASEMAP_ATTRIBUTION,
        },
        'basemap-labels': {
          type: 'raster',
          tiles: [BASEMAP_LABEL_TILES],
          tileSize: 256,
          maxzoom: 16,
        },
      },
      layers: [
        { id: 'bg', type: 'background', paint: { 'background-color': '#0B0A12' } },
        { id: 'basemap-layer', type: 'raster', source: 'basemap', minzoom: 0, maxzoom: 20 },
        { id: 'basemap-labels-layer', type: 'raster', source: 'basemap-labels', minzoom: 0, maxzoom: 20 },
      ],
    },
    center: [0, 20],
    zoom: 2,
  });

  map.addControl(new maplibregl.NavigationControl(), 'top-right');
  map.addControl(new maplibregl.ScaleControl(), 'bottom-right');

  popup = new maplibregl.Popup({ closeButton: false, closeOnClick: false, offset: 15 });

  map.on('load', () => {
    mapLoaded = true;
    ensureLayers();
    applyPaint();
    const queued = queuedMessages;
    queuedMessages = [];
    queued.forEach(handleHostMessage);
  });

  // In viewport mode the host draws only what is visible, so report every settled camera position (debounced).
  map.on('moveend', () => {
    if (!viewportMode) return;
    clearTimeout(viewportTimer);
    viewportTimer = setTimeout(reportViewport, 200);
  });
}

function ensureLayers() {
  const sourceId = 'ops-zones';
  if (!map.getSource(sourceId)) {
    map.addSource(sourceId, { type: 'geojson', data: { type: 'FeatureCollection', features: [] } });
  }
  if (!map.getLayer('zones-fill')) {
    map.addLayer({
      id: 'zones-fill',
      type: 'fill',
      source: sourceId,
      paint: { 'fill-color': ['coalesce', ['get', 'color'], '#888888'], 'fill-opacity': 0.25 },
    });
  }
  if (!map.getLayer('zones-outline')) {
    map.addLayer({
      id: 'zones-outline',
      type: 'line',
      source: sourceId,
      paint: { 'line-color': ['coalesce', ['get', 'color'], '#888888'], 'line-width': 1.5, 'line-opacity': 0.9 },
    });
  }

  map.on('click', 'zones-fill', (e) => {
    if (e.features && e.features[0]) {
      const props = e.features[0].properties;
      if (props.opsId) {
        bridgeCall('click', props.opsId, 'ops');
      } else if (props.aorId) {
        bridgeCall('click', props.aorId, 'aor');
      }
    }
  });

  map.on('mouseenter', 'zones-fill', (e) => {
    map.getCanvas().style.cursor = 'pointer';
    if (e.features && e.features[0] && popup) {
      const props = e.features[0].properties;
      const id = props.opsId || props.aorId;
      if (id) {
        bridgeCall('hover', id);
        const title = props.title || props.name || 'Untitled';
        const html = `
          <div class="space-y-1">
            <p class="font-semibold text-sm">${escapeHtml(title)}</p>
            <p class="font-mono text-xs opacity-70">${escapeHtml(id)}</p>
            <div class="flex gap-3 text-xs opacity-80 pt-1">
              <span>Area: ${formatArea(props.area || 0)}</span>
              ${props.maxAltitude ? `<span>Alt: ${escapeHtml(String(props.maxAltitude))} ${escapeHtml(props.altitudeUnit || '')}</span>` : ''}
            </div>
          </div>
        `;
        popup.setLngLat(e.lngLat).setHTML(html).addTo(map);
      }
    }
  });

  map.on('mouseleave', 'zones-fill', () => {
    map.getCanvas().style.cursor = '';
    if (popup) popup.remove();
    bridgeCall('hover', '');
  });
}

// ---- Data from the host: begin / chunk... / end, so a big update is several modest messages, not one giant string ----

function fmt(n) { return Number(n).toLocaleString('en-US'); }

function reportViewport() {
  if (!map) return;
  const b = map.getBounds();
  postToHost({ kind: 'viewport', west: b.getWest(), south: b.getSouth(), east: b.getEast(), north: b.getNorth(), zoom: map.getZoom() });
}

function updateStatus(begin) {
  let el = document.getElementById('status');
  if (!el) {
    el = document.createElement('div');
    el.id = 'status';
    document.body.appendChild(el);
  }
  if (!begin.viewportMode) { el.style.display = 'none'; return; }
  const inView = begin.inView;
  el.textContent = inView > begin.shown
    ? `${fmt(inView)} in view of ${fmt(begin.total)} · drawing the ${fmt(begin.shown)} largest — zoom in to see all`
    : `${fmt(inView)} in view of ${fmt(begin.total)}`;
  el.style.display = 'block';
}

function commitFeatures() {
  const begin = latestBegin;
  if (!begin) return;
  map.getSource('ops-zones').setData({ type: 'FeatureCollection', features: featureBuffer });
  featureBuffer = [];
  anyFeatureHighlight = !!begin.anyHighlight;
  viewportMode = !!begin.viewportMode;
  applyPaint();
  updateStatus(begin);

  if (begin.fit && begin.fitBounds) {
    const [w, s, e, n] = begin.fitBounds;
    map.fitBounds([[w, s], [e, n]], { padding: 50, maxZoom: 15, duration: 1000 });
    // The camera may not move (already there); make sure the host still learns the viewport.
    if (viewportMode) { clearTimeout(viewportTimer); viewportTimer = setTimeout(reportViewport, 1300); }
  } else if (viewportMode && begin.needsViewport) {
    reportViewport();
  }
}

function handleHostMessage(msg) {
  if (msg.kind === 'highlights') {
    hoverIds = msg.ids || [];
    if (mapLoaded) applyPaint();
    return;
  }
  if (!mapLoaded) { queuedMessages.push(msg); return; }

  switch (msg.kind) {
    case 'begin':
      if (msg.version < currentVersion) return;
      currentVersion = msg.version;
      latestBegin = msg;
      featureBuffer = [];
      break;
    case 'chunk':
      if (msg.version !== currentVersion) return;
      for (let i = 0; i < msg.features.length; i++) featureBuffer.push(msg.features[i]);
      break;
    case 'end':
      if (msg.version !== currentVersion) return;
      commitFeatures();
      break;
  }
}

// Emphasis: features flagged "hl":1 by the host (selection, report matches) plus the hovered/active ids.
const ZONE_COLOR = ['coalesce', ['get', 'color'], '#888888'];

function applyPaint() {
  if (!map || !mapLoaded) return;
  const hasHighlights = anyFeatureHighlight || hoverIds.length > 0;
  const clauses = [['==', ['coalesce', ['get', 'hl'], 0], 1]];
  if (hoverIds.length > 0) {
    clauses.push(['in', ['to-string', ['coalesce', ['get', 'opsId'], '']], ['literal', hoverIds]]);
    clauses.push(['in', ['to-string', ['coalesce', ['get', 'aorId'], '']], ['literal', hoverIds]]);
  }
  const isHighlighted = ['any', ...clauses];
  map.setPaintProperty('zones-fill', 'fill-opacity', hasHighlights ? ['case', isHighlighted, 0.5, 0.15] : 0.25);
  // Highlighted shapes get a Luna-lavender outline so they stand out from every data colour.
  map.setPaintProperty('zones-outline', 'line-color', hasHighlights ? ['case', isHighlighted, '#C4B5FD', ZONE_COLOR] : ZONE_COLOR);
  map.setPaintProperty('zones-outline', 'line-width', hasHighlights ? ['case', isHighlighted, 3, 1] : 1.5);
}

// ---- Entry point for host messages (CEF calls this directly; WebView2 delivers through the listener below) ----

window.lunaHost = function (text) { handleHostMessage(JSON.parse(text)); };

initMap();

if (window.chrome && window.chrome.webview) {
  window.chrome.webview.addEventListener('message', function (e) { handleHostMessage(JSON.parse(e.data)); });
  window.chrome.webview.postMessage(JSON.stringify({ kind: 'ready' }));
}
