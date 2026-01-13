import * as turf from '@turf/turf';
import type { FlightPlan, ParsedFlightPlan, FlightPlanGeoJSON, FlightPlanFeature, FlightPlanProperties, OperationVolume } from '@/types/flightPlan';

// Normalize different API formats to our internal format
function normalizeFlightPlan(raw: Record<string, unknown>): FlightPlan {
  // Handle alternative field names (camelCase vs snake_case)
  const operationPlanId = (raw.operationPlanId || raw.operation_plan_id || '') as string;
  const flightPlanId = (raw.flightPlanId || raw.flight_plan_id) as string | undefined;
  
  // Title can come from different places
  const publicInfo = raw.publicInfo as Record<string, unknown> | undefined;
  const title = (raw.title || publicInfo?.title || '') as string;
  
  // Description from various sources
  const flightDetails = raw.flightDetails as Record<string, unknown> | undefined;
  const description = (raw.description || publicInfo?.description || flightDetails?.flightComment || '') as string;
  
  const state = raw.state as string | undefined;
  const closureReason = raw.closureReason as string | undefined;
  const operator = raw.operator as string | undefined;
  const submitTime = (raw.submitTime || raw.submit_time) as string | undefined;
  const updateTime = (raw.updateTime || raw.update_time) as string | undefined;
  
  // Normalize operation volumes
  const rawVolumes = (raw.operationVolumes || raw.operation_volumes || []) as Record<string, unknown>[];
  const operationVolumes: OperationVolume[] = rawVolumes.map(vol => normalizeVolume(vol));
  
  // Contact details
  const rawContact = raw.contactDetails || raw.contact;
  let contact: FlightPlan['contact'] | undefined;
  if (rawContact && typeof rawContact === 'object') {
    const c = rawContact as Record<string, unknown>;
    contact = {
      name: (c.name || `${c.firstName || ''} ${c.lastName || ''}`.trim()) as string,
      phone: (c.phone || (Array.isArray(c.phones) ? c.phones[0] : undefined)) as string | undefined,
      email: (c.email || (Array.isArray(c.emails) ? c.emails[0] : undefined)) as string | undefined,
    };
  }
  
  return {
    operation_plan_id: operationPlanId,
    flight_plan_id: flightPlanId,
    operator,
    title,
    description,
    state,
    closureReason,
    submit_time: submitTime,
    update_time: updateTime,
    operation_volumes: operationVolumes,
    contact,
  };
}

function normalizeVolume(vol: Record<string, unknown>): OperationVolume {
  // Time fields
  const timeBegin = (vol.timeBegin || vol.effective_time_begin || '') as string;
  const timeEnd = (vol.timeEnd || vol.effective_time_end || '') as string;
  const actualTimeEnd = (vol.actualTimeEnd || vol.actual_time_end) as string | undefined;
  
  // Geometry - can be nested under operationGeometry.geom or directly in operation_geography
  const operationGeometry = vol.operationGeometry as Record<string, unknown> | undefined;
  const directGeography = vol.operation_geography as Record<string, unknown> | undefined;
  
  let geography: OperationVolume['operation_geography'];
  if (operationGeometry?.geom) {
    const geom = operationGeometry.geom as Record<string, unknown>;
    geography = {
      type: geom.type as string,
      coordinates: geom.coordinates as number[][][] | number[][][][],
    };
  } else if (directGeography) {
    geography = {
      type: directGeography.type as string,
      coordinates: directGeography.coordinates as number[][][] | number[][][][],
    };
  }
  
  // Altitude - can be nested under operationGeometry or directly on volume
  const minAltRaw = (operationGeometry?.minAltitude || vol.min_altitude) as Record<string, unknown> | undefined;
  const maxAltRaw = (operationGeometry?.maxAltitude || vol.max_altitude) as Record<string, unknown> | undefined;
  
  const minAltitude = minAltRaw ? {
    altitude_value: (minAltRaw.altitudeValue ?? minAltRaw.altitude_value ?? 0) as number,
    units_of_measure: (minAltRaw.unitsOfMeasure || minAltRaw.units_of_measure || 'FT') as string,
    vertical_reference: (minAltRaw.altitudeType || minAltRaw.vertical_reference) as string | undefined,
  } : undefined;
  
  const maxAltitude = maxAltRaw ? {
    altitude_value: (maxAltRaw.altitudeValue ?? maxAltRaw.altitude_value ?? 0) as number,
    units_of_measure: (maxAltRaw.unitsOfMeasure || maxAltRaw.units_of_measure || 'FT') as string,
    vertical_reference: (maxAltRaw.altitudeType || maxAltRaw.vertical_reference) as string | undefined,
  } : undefined;
  
  return {
    id: (vol.id || vol.alias) as string | undefined,
    ordinal: vol.ordinal as number | undefined,
    effective_time_begin: timeBegin,
    effective_time_end: timeEnd,
    actual_time_end: actualTimeEnd,
    min_altitude: minAltitude,
    max_altitude: maxAltitude,
    operation_geography: geography,
    beyond_visual_line_of_sight: (vol.isBVLOS || vol.beyond_visual_line_of_sight) as boolean | undefined,
  };
}

export function parseFlightPlans(data: unknown): FlightPlan[] {
  let rawPlans: Record<string, unknown>[] = [];
  
  if (Array.isArray(data)) {
    rawPlans = data as Record<string, unknown>[];
  } else if (typeof data === 'object' && data !== null) {
    const obj = data as Record<string, unknown>;
    // Check if it's a single flight plan
    if ('operation_plan_id' in obj || 'operationPlanId' in obj || 'operation_volumes' in obj || 'operationVolumes' in obj) {
      rawPlans = [obj];
    } else if (obj.plans && Array.isArray(obj.plans)) {
      rawPlans = obj.plans as Record<string, unknown>[];
    } else if (obj.operations && Array.isArray(obj.operations)) {
      rawPlans = obj.operations as Record<string, unknown>[];
    } else if (obj.flight_plans && Array.isArray(obj.flight_plans)) {
      rawPlans = obj.flight_plans as Record<string, unknown>[];
    }
  }
  
  if (rawPlans.length === 0) {
    throw new Error('Invalid flight plan data format');
  }
  
  return rawPlans.map(normalizeFlightPlan);
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
        operator: plan.operator,
        title: plan.title || 'Untitled Operation',
        description: plan.description || '',
        state: plan.state,
        closureReason: plan.closureReason,
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
    closureReason: 'NOMINAL',
    operator: 'Test-Operator-123',
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
