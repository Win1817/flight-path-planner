import { useEffect, useRef, useState, useCallback } from 'react';
import maplibregl from 'maplibre-gl';
import 'maplibre-gl/dist/maplibre-gl.css';
import type { FlightPlanGeoJSON } from '@/types/flightPlan';
import { getBoundsFromGeoJSON, formatArea } from '@/utils/flightPlanUtils';
import { FlightPlanProperties } from '@/types/flightPlan';

interface FlightMapProps {
  geojson: FlightPlanGeoJSON | null;
  highlightedPlanIds: Set<string>;
  onZoneClick: (planId: string) => void;
  onZoneHover: (planId: string | null) => void;
}

export function FlightMap({ geojson, highlightedPlanIds, onZoneClick, onZoneHover }: FlightMapProps) {
  const mapContainer = useRef<HTMLDivElement>(null);
  const map = useRef<maplibregl.Map | null>(null);
  const popup = useRef<maplibregl.Popup | null>(null);
  const [mapLoaded, setMapLoaded] = useState(false);

  // Initialize map
  useEffect(() => {
    if (!mapContainer.current || map.current) return;

    map.current = new maplibregl.Map({
      container: mapContainer.current,
      style: {
        version: 8,
        sources: {
          'carto-dark': {
            type: 'raster',
            tiles: [
              'https://a.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}@2x.png',
              'https://b.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}@2x.png',
              'https://c.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}@2x.png',
            ],
            tileSize: 256,
            attribution: '© CARTO © OpenStreetMap contributors',
          },
        },
        layers: [
          {
            id: 'carto-dark-layer',
            type: 'raster',
            source: 'carto-dark',
            minzoom: 0,
            maxzoom: 20,
          },
        ],
      },
      center: [0, 20],
      zoom: 2,
    });

    map.current.addControl(new maplibregl.NavigationControl(), 'top-right');
    map.current.addControl(new maplibregl.ScaleControl(), 'bottom-right');

    popup.current = new maplibregl.Popup({
      closeButton: false,
      closeOnClick: false,
      offset: 15,
    });

    map.current.on('load', () => {
      setMapLoaded(true);
    });

    return () => {
      popup.current?.remove();
      map.current?.remove();
      map.current = null;
    };
  }, []);

  // Update data on map
  useEffect(() => {
    if (!map.current || !mapLoaded || !geojson) return;

    const style = map.current.getStyle();
    if (!style) return;

    const sourceId = 'flight-zones';

    // Manage source
    let source = map.current.getSource(sourceId) as maplibregl.GeoJSONSource;
    if (source) {
      source.setData(geojson);
    } else {
      map.current.addSource(sourceId, {
        type: 'geojson',
        data: geojson,
      });
    }

    // Manage fill layer
    if (!map.current.getLayer('zones-fill')) {
      map.current.addLayer({
        id: 'zones-fill',
        type: 'fill',
        source: sourceId,
        paint: {},
      });
    }

    // Manage outline layer
    if (!map.current.getLayer('zones-outline')) {
      map.current.addLayer({
        id: 'zones-outline',
        type: 'line',
        source: sourceId,
        paint: {},
      });
    }

    // Fit bounds
    const bounds = getBoundsFromGeoJSON(geojson);
    if (bounds) {
      map.current.fitBounds(bounds, {
        padding: { top: 50, bottom: 50, left: 400, right: 50 },
        maxZoom: 15,
        duration: 1000,
      });
    }

    // Click handler
    const clickHandler = (e: maplibregl.MapLayerMouseEvent) => {
      if (e.features && e.features[0]) {
        const props = e.features[0].properties as FlightPlanProperties;
        if (props.flightPlanId) {
          onZoneClick(props.flightPlanId);
        }
      }
    };

    // Hover handlers
    const mouseEnterHandler = (e: maplibregl.MapLayerMouseEvent) => {
      if (!map.current) return;
      map.current.getCanvas().style.cursor = 'pointer';

      if (e.features && e.features[0] && popup.current) {
        const props = e.features[0].properties as FlightPlanProperties;
        const id = props?.flightPlanId;
        if (id) {
          onZoneHover(id);

          const html = `
            <div class="space-y-1">
              <p class="font-semibold text-sm">${props?.title || 'Untitled'}</p>
              <p class="font-mono text-xs opacity-70">${id}</p>
              <div class="flex gap-3 text-xs opacity-80 pt-1">
                <span>Area: ${formatArea(props?.area || 0)}</span>
                ${props?.maxAltitude ? `<span>Alt: ${props.maxAltitude} ${props.altitudeUnit}</span>` : ''}
              </div>
            </div>
          `;

          popup.current
            .setLngLat(e.lngLat)
            .setHTML(html)
            .addTo(map.current);
        }
      }
    };

    const mouseLeaveHandler = () => {
      if (!map.current) return;
      map.current.getCanvas().style.cursor = '';
      popup.current?.remove();
      onZoneHover(null);
    };
    
    map.current.off('click', 'zones-fill', clickHandler);
    map.current.on('click', 'zones-fill', clickHandler);
    
    map.current.off('mouseenter', 'zones-fill', mouseEnterHandler);
    map.current.on('mouseenter', 'zones-fill', mouseEnterHandler);

    map.current.off('mouseleave', 'zones-fill', mouseLeaveHandler);
    map.current.on('mouseleave', 'zones-fill', mouseLeaveHandler);

  }, [geojson, mapLoaded, onZoneClick, onZoneHover]);

  // Update highlight styling
  const updateHighlighting = useCallback(() => {
    if (!map.current || !mapLoaded) return;

    const highlightedArray = Array.from(highlightedPlanIds);
    const hasHighlights = highlightedArray.length > 0;

    map.current.setPaintProperty('zones-fill', 'fill-color', ['get', 'color']);
    map.current.setPaintProperty('zones-fill', 'fill-opacity', 
      hasHighlights
        ? ['case', ['in', ['get', 'flightPlanId'], ['literal', highlightedArray]], 0.5, 0.15]
        : 0.25
    );

    map.current.setPaintProperty('zones-outline', 'line-color', ['get', 'color']);
    map.current.setPaintProperty('zones-outline', 'line-width',
      hasHighlights
        ? ['case', ['in', ['get', 'flightPlanId'], ['literal', highlightedArray]], 3, 1]
        : 1.5
    );
    map.current.setPaintProperty('zones-outline', 'line-opacity', 0.9);

  }, [highlightedPlanIds, mapLoaded]);

  useEffect(() => {
    if (map.current?.isStyleLoaded()) {
      updateHighlighting();
    }
  }, [updateHighlighting]);

  return (
    <div ref={mapContainer} className="w-full h-full" />
  );
}
