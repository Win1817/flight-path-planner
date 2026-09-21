// Ported from src/components/FlightMap.tsx — keep in sync with that file's map setup.
// Driven by two functions called from C# (via AvaloniaCefBrowser.ExecuteJavaScript):
//   window.updateMapData(geojsonJsonString)
//   window.updateHighlights(idsJsonArrayString)
// and calls back into C# via the registered bridge object:
//   window.csharpBridge.OnZoneClick(id, dataType)
//   window.csharpBridge.OnZoneHover(idOrNull)

// Talks to C#: WebView2 (Windows) posts messages; CEF (Linux/macOS) exposes window.csharpBridge.
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
let pendingData = null;
let pendingHighlights = null;

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
    if (pendingData) { applyData(pendingData); pendingData = null; }
    if (pendingHighlights) { applyHighlights(pendingHighlights); pendingHighlights = null; }
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

function applyData(geojson) {
  const source = map.getSource('ops-zones');
  source.setData(geojson);

  const bounds = getBoundsFromGeoJSON(geojson);
  if (bounds) {
    map.fitBounds(bounds, { padding: { top: 50, bottom: 50, left: 400, right: 50 }, maxZoom: 15, duration: 1000 });
  }
}

const ZONE_COLOR = ['coalesce', ['get', 'color'], '#888888'];

function applyHighlights(ids) {
  const hasHighlights = ids.length > 0;
  const isHighlighted = ['any', ['in', ['get', 'opsId'], ['literal', ids]], ['in', ['get', 'aorId'], ['literal', ids]]];
  map.setPaintProperty('zones-fill', 'fill-opacity', hasHighlights ? ['case', isHighlighted, 0.5, 0.15] : 0.25);
  // Highlighted shapes get a Luna-lavender outline so they stand out from every data colour.
  map.setPaintProperty('zones-outline', 'line-color', hasHighlights ? ['case', isHighlighted, '#C4B5FD', ZONE_COLOR] : ZONE_COLOR);
  map.setPaintProperty('zones-outline', 'line-width', hasHighlights ? ['case', isHighlighted, 3, 1] : 1.5);
}

// ---- Entry points called from C# ----

window.updateMapData = function (geojsonJson) {
  const geojson = JSON.parse(geojsonJson);
  if (!mapLoaded) { pendingData = geojson; return; }
  applyData(geojson);
};

window.updateHighlights = function (idsJson) {
  const ids = JSON.parse(idsJson);
  if (!mapLoaded) { pendingHighlights = ids; return; }
  applyHighlights(ids);
};

initMap();

if (window.chrome && window.chrome.webview) {
  window.chrome.webview.addEventListener('message', function (e) {
    const message = JSON.parse(e.data);
    if (message.kind === 'data') window.updateMapData(message.payload);
    else if (message.kind === 'highlights') window.updateHighlights(message.payload);
  });
  window.chrome.webview.postMessage(JSON.stringify({ kind: 'ready' }));
}
