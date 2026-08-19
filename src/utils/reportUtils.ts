import * as turf from '@turf/turf';
import type { Feature, Polygon, MultiPolygon } from 'geojson';
import type { ParsedOps, ParsedAor } from '@/types/ops';

function toTurfFeature(type: string, coordinates: number[][][] | number[][][][]): Feature<Polygon | MultiPolygon> | null {
  try {
    if (type === 'Polygon') {
      return turf.polygon(coordinates as number[][][]);
    }
    if (type === 'MultiPolygon') {
      return turf.multiPolygon(coordinates as number[][][][]);
    }
    return null;
  } catch {
    return null;
  }
}

interface OpFeatureCache {
  op: ParsedOps;
  features: Feature<Polygon | MultiPolygon>[];
}

function buildOpFeatureCache(ops: ParsedOps[]): OpFeatureCache[] {
  return ops.map(op => {
    const allVolumes = [...(op.operation_volumes || []), ...(op.off_nominal_volumes || [])];
    const features = allVolumes
      .filter(v => v.operation_geography)
      .map(v => toTurfFeature(v.operation_geography!.type, v.operation_geography!.coordinates))
      .filter((f): f is Feature<Polygon | MultiPolygon> => f !== null);
    return { op, features };
  });
}

function cachedOpIntersectsAor(cached: OpFeatureCache, aorFeature: Feature<Polygon | MultiPolygon>): boolean {
  return cached.features.some(f => {
    try {
      return turf.booleanIntersects(f, aorFeature);
    } catch {
      return false;
    }
  });
}

// Returns the ops whose operation volumes geographically intersect the given AoR.
export function getOpsInAor(ops: ParsedOps[], aor: ParsedAor): ParsedOps[] {
  const aorFeature = toTurfFeature(aor.geometry.type, aor.geometry.coordinates);
  if (!aorFeature) return [];
  const cache = buildOpFeatureCache(ops);
  return cache.filter(c => cachedOpIntersectsAor(c, aorFeature)).map(c => c.op);
}

export interface AorReportRow {
  aor: ParsedAor;
  matchCount: number;
}

// Returns, for every AoR, how many of the given ops geographically intersect it.
export function getAorReportSummary(aors: ParsedAor[], ops: ParsedOps[]): AorReportRow[] {
  const opCache = buildOpFeatureCache(ops);
  return aors.map(aor => {
    const aorFeature = toTurfFeature(aor.geometry.type, aor.geometry.coordinates);
    if (!aorFeature) return { aor, matchCount: 0 };
    const matchCount = opCache.filter(c => cachedOpIntersectsAor(c, aorFeature)).length;
    return { aor, matchCount };
  });
}

// Returns the ops whose operation volumes fall within radiusKm of centerLngLat.
export function getOpsNearPoint(ops: ParsedOps[], centerLngLat: [number, number], radiusKm: number): ParsedOps[] {
  const circle = turf.circle(centerLngLat, radiusKm, { steps: 64, units: 'kilometers' });
  const cache = buildOpFeatureCache(ops);
  return cache.filter(c => cachedOpIntersectsAor(c, circle)).map(c => c.op);
}
