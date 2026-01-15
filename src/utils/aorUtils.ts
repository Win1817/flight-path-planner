import * as turf from '@turf/turf';
import type { Aor, ParsedAor, AorGeoJSON, AorFeature, AorProperties } from '@/types/flightPlan';

// Basic validation for raw AoR data
function isValidAor(raw: any): raw is Record<string, unknown> {
  return typeof raw === 'object' && raw !== null &&
         typeof raw.id === 'string' &&
         typeof raw.name === 'string' &&
         typeof raw.designator === 'string' &&
         typeof raw.geometry === 'object' &&
         typeof raw.lowerLimit === 'number' &&
         typeof raw.upperLimit === 'number';
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
    geometry: raw.geometry as any, // Consider adding stronger validation here
    extendedGeometry: raw.extendedGeometry as any,
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
    if (isValidAor(data)) {
        rawAors = [data];
    } else if ('aors' in data && Array.isArray((data as any).aors)) {
        rawAors = (data as any).aors;
    } else if ('responsibility_areas' in data && Array.isArray((data as any).responsibility_areas)) {
        rawAors = (data as any).responsibility_areas;
    }
  }

  if (rawAors.length === 0) {
    throw new Error('No valid AoR data found in the file');
  }

  return rawAors.map(normalizeAor);
}

// Calculate area of a polygon or multipolygon
export function calculateAorArea(geometry: any): number {
  try {
    if (geometry.type === 'Polygon') {
      const polygon = turf.polygon(geometry.coordinates);
      return turf.area(polygon);
    } else if (geometry.type === 'MultiPolygon') {
      const multiPolygon = turf.multiPolygon(geometry.coordinates);
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

    return {
      type: 'Feature',
      properties,
      geometry: aor.geometry, // Assuming geometry is already a valid GeoJSON geometry
    };
  });

  return {
    type: 'FeatureCollection',
    features,
  };
}
