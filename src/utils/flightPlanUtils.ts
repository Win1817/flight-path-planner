import * as turf from '@turf/turf';
import type { FlightPlan, ParsedFlightPlan, FlightPlanGeoJSON, FlightPlanFeature, FlightPlanProperties, OperationVolume } from '@/types/flightPlan';

export function parseFlightPlans(data: unknown): FlightPlan[] {
  if (Array.isArray(data)) {
    return data as FlightPlan[];
  }
  if (typeof data === 'object' && data !== null) {
    // Check if it's a single flight plan
    if ('operation_plan_id' in data || 'operation_volumes' in data) {
      return [data as FlightPlan];
    }
    // Check if it has a plans/operations array
    const obj = data as Record<string, unknown>;
    if (obj.plans && Array.isArray(obj.plans)) {
      return obj.plans as FlightPlan[];
    }
    if (obj.operations && Array.isArray(obj.operations)) {
      return obj.operations as FlightPlan[];
    }
    if (obj.flight_plans && Array.isArray(obj.flight_plans)) {
      return obj.flight_plans as FlightPlan[];
    }
  }
  throw new Error('Invalid flight plan data format');
}

export function calculatePolygonArea(coordinates: number[][][] | number[][][][], type: string): number {
  try {
    if (type === 'Polygon') {
      const polygon = turf.polygon(coordinates as number[][][]);
      return turf.area(polygon);
    } else if (type === 'MultiPolygon') {
      const multiPolygon = turf.multiPolygon(coordinates as number[][][][]);
      return turf.area(multiPolygon);
    }
    return 0;
  } catch {
    return 0;
  }
}

export function getVolumeTimeRange(volumes: OperationVolume[]): { start: Date; end: Date } {
  const times = volumes.flatMap(v => [
    new Date(v.effective_time_begin),
    new Date(v.effective_time_end)
  ]).filter(d => !isNaN(d.getTime()));

  if (times.length === 0) {
    return { start: new Date(), end: new Date() };
  }

  return {
    start: new Date(Math.min(...times.map(t => t.getTime()))),
    end: new Date(Math.max(...times.map(t => t.getTime())))
  };
}

export function processFlightPlan(plan: FlightPlan): ParsedFlightPlan {
  const allVolumes = [...(plan.operation_volumes || []), ...(plan.off_nominal_volumes || [])];
  const { start, end } = getVolumeTimeRange(allVolumes);
  
  let totalArea = 0;
  allVolumes.forEach(volume => {
    if (volume.operation_geography) {
      totalArea += calculatePolygonArea(
        volume.operation_geography.coordinates,
        volume.operation_geography.type
      );
    }
  });

  return {
    ...plan,
    computedArea: totalArea,
    startTime: start,
    endTime: end,
    zoneCount: allVolumes.filter(v => v.operation_geography).length
  };
}

export function flightPlansToGeoJSON(plans: ParsedFlightPlan[]): FlightPlanGeoJSON {
  const features: FlightPlanFeature[] = [];

  plans.forEach(plan => {
    const allVolumes = [...(plan.operation_volumes || []), ...(plan.off_nominal_volumes || [])];
    
    allVolumes.forEach((volume, index) => {
      if (!volume.operation_geography) return;

      const area = calculatePolygonArea(
        volume.operation_geography.coordinates,
        volume.operation_geography.type
      );

      const properties: FlightPlanProperties = {
        flightPlanId: plan.flight_plan_id || plan.operation_plan_id,
        operationPlanId: plan.operation_plan_id,
        title: plan.title || 'Untitled Operation',
        description: plan.description || '',
        volumeIndex: index,
        minAltitude: volume.min_altitude?.altitude_value ?? null,
        maxAltitude: volume.max_altitude?.altitude_value ?? null,
        altitudeUnit: volume.max_altitude?.units_of_measure || volume.min_altitude?.units_of_measure || 'FT',
        startTime: volume.effective_time_begin,
        endTime: volume.effective_time_end,
        area
      };

      const geometry = volume.operation_geography.type === 'Polygon' 
        ? { type: 'Polygon' as const, coordinates: volume.operation_geography.coordinates as number[][][] }
        : { type: 'MultiPolygon' as const, coordinates: volume.operation_geography.coordinates as number[][][][] };

      features.push({
        type: 'Feature',
        properties,
        geometry
      });
    });
  });

  return {
    type: 'FeatureCollection',
    features
  };
}

export function formatArea(squareMeters: number): string {
  if (squareMeters >= 10000) {
    return `${(squareMeters / 10000).toFixed(2)} ha`;
  }
  return `${squareMeters.toFixed(0)} m²`;
}

export function formatDateTime(date: Date | string): string {
  const d = typeof date === 'string' ? new Date(date) : date;
  return d.toISOString().replace('T', ' ').slice(0, 19) + ' UTC';
}

export function formatDateTimeShort(date: Date | string): string {
  const d = typeof date === 'string' ? new Date(date) : date;
  return d.toISOString().slice(0, 16).replace('T', ' ');
}

export function getOperationStatus(startTime: Date, endTime: Date): 'active' | 'pending' | 'expired' {
  const now = new Date();
  if (now < startTime) return 'pending';
  if (now > endTime) return 'expired';
  return 'active';
}

export function getBoundsFromGeoJSON(geojson: FlightPlanGeoJSON): [[number, number], [number, number]] | null {
  if (geojson.features.length === 0) return null;

  try {
    const bbox = turf.bbox(geojson as GeoJSON.FeatureCollection);
    return [[bbox[0], bbox[1]], [bbox[2], bbox[3]]];
  } catch {
    return null;
  }
}

// Generate sample data for testing
export function generateSampleFlightPlan(): FlightPlan {
  const now = new Date();
  const later = new Date(now.getTime() + 2 * 60 * 60 * 1000);

  return {
    operation_plan_id: `OP-${Math.random().toString(36).substr(2, 9).toUpperCase()}`,
    title: 'Sample Drone Survey Mission',
    description: 'Aerial photography and mapping operation for agricultural land survey.',
    state: 'ACCEPTED',
    submit_time: new Date(now.getTime() - 24 * 60 * 60 * 1000).toISOString(),
    update_time: now.toISOString(),
    operation_volumes: [
      {
        id: 'vol-001',
        ordinal: 0,
        effective_time_begin: now.toISOString(),
        effective_time_end: later.toISOString(),
        min_altitude: {
          altitude_value: 50,
          units_of_measure: 'FT',
          vertical_reference: 'AGL'
        },
        max_altitude: {
          altitude_value: 400,
          units_of_measure: 'FT',
          vertical_reference: 'AGL'
        },
        operation_geography: {
          type: 'Polygon',
          coordinates: [[
            [-122.4194, 37.7749],
            [-122.4094, 37.7749],
            [-122.4094, 37.7849],
            [-122.4194, 37.7849],
            [-122.4194, 37.7749]
          ]]
        }
      }
    ]
  };
}
