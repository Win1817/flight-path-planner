export interface GeocodeResult {
  lat: number;
  lng: number;
  displayName: string;
}

// Matches "lat,long", "lat:long", or "lat long" (with optional decimals/negatives).
const COORD_PATTERN = /^\s*(-?\d+(?:\.\d+)?)\s*[,:\s]\s*(-?\d+(?:\.\d+)?)\s*$/;

export function parseCoordinates(query: string): GeocodeResult | null {
  const match = query.match(COORD_PATTERN);
  if (!match) return null;

  const lat = parseFloat(match[1]);
  const lng = parseFloat(match[2]);
  if (isNaN(lat) || isNaN(lng) || lat < -90 || lat > 90 || lng < -180 || lng > 180) {
    return null;
  }

  return { lat, lng, displayName: `${lat.toFixed(5)}, ${lng.toFixed(5)}` };
}

interface NominatimResult {
  lat: string;
  lon: string;
  display_name: string;
}

// Free, keyless geocoding via OpenStreetMap's Nominatim public API.
async function geocodeAddress(query: string): Promise<GeocodeResult[]> {
  const url = `https://nominatim.openstreetmap.org/search?format=json&limit=5&q=${encodeURIComponent(query)}`;
  const response = await fetch(url, { headers: { 'Accept': 'application/json' } });

  if (!response.ok) {
    throw new Error('Geocoding request failed. Please try again.');
  }

  const data = await response.json() as NominatimResult[];
  return data.map(item => ({
    lat: parseFloat(item.lat),
    lng: parseFloat(item.lon),
    displayName: item.display_name,
  }));
}

// Resolves a search query to one or more candidate locations: parses it as
// raw coordinates first, falling back to address geocoding.
export async function resolveLocation(query: string): Promise<GeocodeResult[]> {
  const direct = parseCoordinates(query);
  if (direct) return [direct];
  return geocodeAddress(query);
}
