import * as turf from '@turf/turf';
import type { Aor, ParsedAor, AorGeoJSON, AorFeature, AorProperties, AorGeometry } from '@/types/ops';
import { ensureUtc } from '@/utils/opsUtils';

const AOR_COLORS = [
  '#FFD700', // gold
  '#FB923C', // orange-400
  '#F87171', // red-400
  '#C084FC', // purple-400
  '#38BDF8', // sky-400
  '#4ADE80', // green-400
  '#F472B6', // pink-400
  '#A78BFA', // violet-400
  '#FCD34D', // amber-300
  '#2DD4BF', // teal-400
];

// Legacy "responsibility area" schema: geometry is a single object, limits are top-level.
function isLegacyAor(raw: Record<string, unknown>): boolean {
  return typeof raw.id === 'string' &&
         typeof raw.name === 'string' &&
         typeof raw.designator === 'string' &&
         typeof raw.geometry === 'object' && raw.geometry !== null && !Array.isArray(raw.geometry) &&
         typeof raw.lowerLimit === 'number' &&
         typeof raw.upperLimit === 'number';
}

// Real-world "airspace zone" schema: geometry is an array of {horizontalProjection, lowerLimit, upperLimit, ...}.
function isZoneAor(raw: Record<string, unknown>): boolean {
  return (typeof raw.name === 'string' || typeof raw.identifier === 'string') &&
         Array.isArray(raw.geometry) && raw.geometry.length > 0;
}

function normalizeLegacyAor(raw: Record<string, unknown>): Aor {
  return {
    id: raw.id as string,
    name: raw.name as string,
    designator: raw.designator as string,
    geometry: raw.geometry as AorGeometry,
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

interface HorizontalProjection {
  type: string;
  coordinates?: number[][][] | number[][][][];
  center?: number[];
  radius?: number; // meters
}

function projectionToPolygons(projection: HorizontalProjection): number[][][][] {
  if (projection.type === 'Circle' && projection.center && typeof projection.radius === 'number') {
    const circle = turf.circle(projection.center, projection.radius / 1000, { steps: 64, units: 'kilometers' });
    return [circle.geometry.coordinates];
  }
  if (projection.type === 'Polygon' && projection.coordinates) {
    return [projection.coordinates as number[][][]];
  }
  if (projection.type === 'MultiPolygon' && projection.coordinates) {
    return projection.coordinates as number[][][][];
  }
  return [];
}

function normalizeZoneAor(raw: Record<string, unknown>): Aor {
  const geometryEntries = raw.geometry as Record<string, unknown>[];

  const polygons: number[][][][] = [];
  let lowerLimit = Infinity;
  let upperLimit = -Infinity;
  let verticalLimitsUom = 'FT';
  let verticalReferenceType = '';

  geometryEntries.forEach(entry => {
    const projection = entry.horizontalProjection as HorizontalProjection | undefined;
    if (projection) {
      polygons.push(...projectionToPolygons(projection));
    }
    if (typeof entry.lowerLimit === 'number') lowerLimit = Math.min(lowerLimit, entry.lowerLimit);
    if (typeof entry.upperLimit === 'number') upperLimit = Math.max(upperLimit, entry.upperLimit);
    if (typeof entry.uomDimensions === 'string') verticalLimitsUom = entry.uomDimensions;
    if (typeof entry.lowerVerticalReference === 'string') verticalReferenceType = entry.lowerVerticalReference;
    else if (typeof entry.upperVerticalReference === 'string') verticalReferenceType = entry.upperVerticalReference;
  });

  if (polygons.length === 0) {
    throw new Error('AoR zone has no usable geometry');
  }

  const geometry: AorGeometry = polygons.length === 1
    ? { type: 'Polygon', coordinates: polygons[0] }
    : { type: 'MultiPolygon', coordinates: polygons };

  const applicability = Array.isArray(raw.applicability) ? raw.applicability[0] as Record<string, unknown> : undefined;

  const id = (raw.zoneId || raw.identifier || raw.name) as string;
  const name = (raw.name || raw.identifier || id) as string;
  const designator = (raw.identifier || raw.name || id) as string;

  return {
    id,
    name,
    designator,
    geometry,
    lowerLimit: isFinite(lowerLimit) ? lowerLimit : 0,
    upperLimit: isFinite(upperLimit) ? upperLimit : 0,
    verticalLimitsUom,
    verticalReferenceType,
    featureType: raw.type as string | undefined,
    restriction: raw.restriction as string | undefined,
    reasons: raw.reason as string[] | undefined,
    message: raw.message as string | undefined,
    effectiveTimeBegin: ensureUtc(applicability?.startDateTime as string | undefined),
    effectiveTimeEnd: ensureUtc(applicability?.endDateTime as string | undefined),
  };
}

// Normalize a single AoR object, supporting both known schemas.
function normalizeAor(raw: Record<string, unknown>): Aor | null {
  try {
    if (isLegacyAor(raw)) {
      return normalizeLegacyAor(raw);
    }
    if (isZoneAor(raw)) {
      return normalizeZoneAor(raw);
    }
    return null;
  } catch {
    return null;
  }
}

export interface ParseAorsResult {
  aors: Aor[];
  skipped: number; // entries that looked like AoRs but failed to normalize (e.g. unsupported geometry type)
}

// Parse a file that could contain one or multiple AoRs
export function parseAors(data: unknown): ParseAorsResult {
  let rawAors: Record<string, unknown>[] = [];

  if (Array.isArray(data)) {
    rawAors = data as Record<string, unknown>[];
  } else if (typeof data === 'object' && data !== null) {
    const obj = data as Record<string, unknown>;
    if (isLegacyAor(obj) || isZoneAor(obj)) {
        rawAors = [obj];
    } else if (Array.isArray(obj.aors)) {
        rawAors = obj.aors as Record<string, unknown>[];
    } else if (Array.isArray(obj.responsibility_areas)) {
        rawAors = obj.responsibility_areas as Record<string, unknown>[];
    } else if (Array.isArray(obj.zones)) {
        rawAors = obj.zones as Record<string, unknown>[];
    }
  }

  const normalized = rawAors.map(normalizeAor);
  const aors = normalized.filter((a): a is Aor => a !== null);

  if (aors.length === 0) {
    throw new Error('No valid AoR data found in the file');
  }

  return { aors, skipped: normalized.length - aors.length };
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

// Process a normalized AoR to add computed properties like area and a display color
export function processAor(aor: Aor, index = 0): ParsedAor {
  const area = calculateAorArea(aor.geometry);
  return {
    ...aor,
    computedArea: area,
    color: AOR_COLORS[index % AOR_COLORS.length],
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
      color: aor.color || '#FFD700'
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
