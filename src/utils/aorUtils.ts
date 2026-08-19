import * as turf from '@turf/turf';
import type { Aor, ParsedAor, AorGeoJSON, AorFeature, AorProperties, AorGeometry } from '@/types/ops';

// Basic validation for raw AoR data
function isValidAor(raw: unknown): raw is Record<string, unknown> {
  if (typeof raw !== 'object' || raw === null) return false;
  const r = raw as Record<string, unknown>;
  return typeof r.id === 'string' &&
         typeof r.name === 'string' &&
         typeof r.designator === 'string' &&
         typeof r.geometry === 'object' &&
         typeof r.lowerLimit === 'number' &&
         typeof r.upperLimit === 'number';
}

// Normalize a single AoR object
function normalizeAor(raw: Record<string, unknown>): Aor {
  if (!isValidAor(raw)) {
    throw new Error('Invalid AoR data structure');
  }
  return {
    id: raw.id as string,
    name: raw.name as string,
    designator: raw.designator as string,
    geometry: raw.geometry as AorGeometry, // Consider adding stronger validation here
    extendedGeometry: raw.extendedGeometry as AorGeometry | undefined,
    lowerLimit: raw.lowerLimit as number,
    upperLimit: raw.upperLimit as number,
    verticalLimitsUom: raw.verticalLimitsUom as string,
    verticalReferenceType: raw.verticalReferenceType as string,
    autoReject: raw.autoReject as boolean,
    autoApprovalEnabled: raw.autoApprovalEnabled as boolean,
    aorEnabled: raw.aorEnabled as boolean,
    autoTakeOffClearanceEnabled: raw.autoTakeOffClearanceEnabled as boolean,
    maxSimultaneousOperationsEnabled: raw.maxSimultaneousOperationsEnabled as boolean,
    maxSimultaneousOperations: raw.maxSimultaneousOperations as number,
    featureType: raw.featureType as string,
  };
}

// Parse a file that could contain one or multiple AoRs
export function parseAors(data: unknown): Aor[] {
  let rawAors: Record<string, unknown>[] = [];

  if (Array.isArray(data)) {
    rawAors = data as Record<string, unknown>[];
  } else if (typeof data === 'object' && data !== null) {
    // Handle nested structures if necessary, e.g., data.aors
    const obj = data as Record<string, unknown>;
    if (isValidAor(data)) {
        rawAors = [data];
    } else if (Array.isArray(obj.aors)) {
        rawAors = obj.aors as Record<string, unknown>[];
    } else if (Array.isArray(obj.responsibility_areas)) {
        rawAors = obj.responsibility_areas as Record<string, unknown>[];
    }
  }

  if (rawAors.length === 0) {
    throw new Error('No valid AoR data found in the file');
  }

  return rawAors.map(normalizeAor);
}

// Calculate area of a polygon or multipolygon
export function calculateAorArea(geometry: AorGeometry): number {
  try {
    if (geometry.type === 'Polygon') {
      const polygon = turf.polygon(geometry.coordinates as number[][][]);
      return turf.area(polygon);
    } else if (geometry.type === 'MultiPolygon') {
      const multiPolygon = turf.multiPolygon(geometry.coordinates as number[][][][]);
      return turf.area(multiPolygon);
    }
    return 0;
  } catch (error) {
    console.error('Error calculating AoR area:', error);
    return 0;
  }
}

// Process a normalized AoR to add computed properties like area
export function processAor(aor: Aor): ParsedAor {
  const area = calculateAorArea(aor.geometry);
  return {
    ...aor,
    computedArea: area,
  };
}

// Convert an array of parsed AoRs into a GeoJSON FeatureCollection
export function aorsToGeoJSON(aors: ParsedAor[]): AorGeoJSON {
  const features: AorFeature[] = aors.map(aor => {
    const properties: AorProperties = {
      dataType: 'aor',
      aorId: aor.id,
      name: aor.name,
      designator: aor.designator,
      lowerLimit: aor.lowerLimit,
      upperLimit: aor.upperLimit,
      limitUnit: aor.verticalLimitsUom,
      verticalReference: aor.verticalReferenceType,
      area: aor.computedArea,
      color: '#FFD700' // Example color for AoRs
    };

    const geometry = aor.geometry.type === 'Polygon'
      ? { type: 'Polygon' as const, coordinates: aor.geometry.coordinates as number[][][] }
      : { type: 'MultiPolygon' as const, coordinates: aor.geometry.coordinates as number[][][][] };

    return {
      type: 'Feature',
      properties,
      geometry,
    };
  });

  return {
    type: 'FeatureCollection',
    features,
  };
}
