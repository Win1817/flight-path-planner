import { useEffect, useRef, useState, useCallback } from 'react';
import maplibregl from 'maplibre-gl';
import 'maplibre-gl/dist/maplibre-gl.css';
import type { FlightPlanGeoJSON, FlightPlanProperties } from '@/types/flightPlan';
import { getBoundsFromGeoJSON, formatArea } from '@/utils/flightPlanUtils';

interface FlightMapProps {
  geojson: FlightPlanGeoJSON | null;
  highlightedPlanIds: Set<string>;
  onZoneClick: (planId: string) => void;
  onZoneHover: (planId: string | null) => void;
}

const ZONE_COLORS = [
  '#3B82F6', // blue
  '#10B981', // emerald
  '#F59E0B', // amber
  '#EF4444', // red
  '#8B5CF6', // violet
  '#EC4899', // pink
  '#06B6D4', // cyan
];

export function FlightMap({ geojson, highlightedPlanIds, onZoneClick, onZoneHover }: FlightMapProps) {
  const mapContainer = useRef<HTMLDivElement>(null);
  const map = useRef<maplibregl.Map | null>(null);
  const popup = useRef<maplibregl.Popup | null>(null);
  const [mapLoaded, setMapLoaded] = useState(false);
  const highlightedIdsRef = useRef<Set<string>>(highlightedPlanIds);

  // Keep ref in sync
  useEffect(() => {
    highlightedIdsRef.current = highlightedPlanIds;
  }, [highlightedPlanIds]);

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

    // Remove existing layers and sources
    const existingLayers = style.layers || [];
    existingLayers.forEach(layer => {
      if (layer.id.startsWith('zones-')) {
        map.current?.removeLayer(layer.id);
      }
    });
    if (map.current.getSource('flight-zones')) {
      map.current.removeSource('flight-zones');
    }

    // Assign colors to each unique plan
    const planIds = [...new Set(geojson.features.map(f => f.properties.operationPlanId))];
    const colorMap: Record<string, string> = {};
    planIds.forEach((id, index) => {
      colorMap[id] = ZONE_COLORS[index % ZONE_COLORS.length];
    });

    // Add colored features
    const coloredFeatures = geojson.features.map(feature => {
      const props: FlightPlanProperties = {
        ...feature.properties,
        color: colorMap[feature.properties.operationPlanId],
      };
      return {
        type: 'Feature' as const,
        properties: props,
        geometry: feature.geometry,
      };
    });

    map.current.addSource('flight-zones', {
      type: 'geojson',
      data: {
        type: 'FeatureCollection' as const,
        features: coloredFeatures,
      },
    });

    // Fill layer
    map.current.addLayer({
      id: 'zones-fill',
      type: 'fill',
      source: 'flight-zones',
      paint: {
        'fill-color': ['get', 'color'],
        'fill-opacity': 0.25,
      },
    });

    // Outline layer
    map.current.addLayer({
      id: 'zones-outline',
      type: 'line',
      source: 'flight-zones',
      paint: {
        'line-color': ['get', 'color'],
        'line-width': 1.5,
        'line-opacity': 0.9,
      },
    });

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
    map.current.on('click', 'zones-fill', (e) => {
      if (e.features && e.features[0]) {
        const planId = e.features[0].properties?.operationPlanId;
        if (planId) {
          onZoneClick(planId);
        }
      }
    });

    // Hover handlers
    map.current.on('mouseenter', 'zones-fill', (e) => {
      if (!map.current) return;
      map.current.getCanvas().style.cursor = 'pointer';

      if (e.features && e.features[0] && popup.current) {
        const props = e.features[0].properties;
        const planId = props?.operationPlanId;
        if (planId) {
          onZoneHover(planId);

          const html = `
            <div class="space-y-1">
              <p class="font-semibold text-sm">${props?.title || 'Untitled'}</p>
              <p class="font-mono text-xs opacity-70">${planId}</p>
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
    });

    map.current.on('mouseleave', 'zones-fill', () => {
      if (!map.current) return;
      map.current.getCanvas().style.cursor = '';
      popup.current?.remove();
      onZoneHover(null);
    });

  }, [geojson, mapLoaded, onZoneClick, onZoneHover]);

  // Update highlight styling
  const updateHighlighting = useCallback(() => {
    if (!map.current || !mapLoaded) return;

    const highlightedArray = Array.from(highlightedPlanIds);
    const hasHighlights = highlightedArray.length > 0;

    if (map.current.getLayer('zones-fill')) {
      map.current.setPaintProperty('zones-fill', 'fill-opacity', 
        hasHighlights
          ? ['case', ['in', ['get', 'operationPlanId'], ['literal', highlightedArray]], 0.5, 0.15]
          : 0.25
      );
    }

    if (map.current.getLayer('zones-outline')) {
      map.current.setPaintProperty('zones-outline', 'line-width',
        hasHighlights
          ? ['case', ['in', ['get', 'operationPlanId'], ['literal', highlightedArray]], 3, 1]
          : 1.5
      );
    }
  }, [highlightedPlanIds, mapLoaded]);

  useEffect(() => {
    updateHighlighting();
  }, [updateHighlighting]);

  return (
    <div ref={mapContainer} className="w-full h-full" />
  );
}
