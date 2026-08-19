import { Crosshair, MapPin, Search, Loader2, X, AlertCircle, Download } from 'lucide-react';
import { Input } from '@/components/ui/input';
import { Button } from '@/components/ui/button';
import type { ParsedOps } from '@/types/ops';
import type { GeocodeResult } from '@/utils/geocodeUtils';
import { MatchedOpsList } from '@/components/MatchedOpsList';

const RADIUS_PRESETS_KM = [0.5, 1, 2, 5, 10];

interface FlightLookupProps {
  hasOps: boolean;
  query: string;
  onQueryChange: (query: string) => void;
  radiusKm: number;
  onRadiusChange: (km: number) => void;
  onSearch: () => void;
  loading: boolean;
  error: string | null;
  center: GeocodeResult | null;
  candidates: GeocodeResult[] | null;
  onSelectCandidate: (result: GeocodeResult) => void;
  onClear: () => void;
  matchingOps: ParsedOps[];
  activeOpId: string | null;
  hoveredOpId: string | null;
  onActivateOp: (id: string) => void;
  onHoverOp: (id: string | null) => void;
  onExport: (format: 'json' | 'xlsx') => void;
}

export function FlightLookup({
  hasOps,
  query,
  onQueryChange,
  radiusKm,
  onRadiusChange,
  onSearch,
  loading,
  error,
  center,
  candidates,
  onSelectCandidate,
  onClear,
  matchingOps,
  activeOpId,
  hoveredOpId,
  onActivateOp,
  onHoverOp,
  onExport,
}: FlightLookupProps) {
  if (!hasOps) {
    return (
      <div className="flex flex-col items-center justify-center py-12 text-center">
        <div className="p-4 rounded-full bg-muted/50 mb-4">
          <Crosshair className="w-8 h-8 text-muted-foreground" />
        </div>
        <p className="text-sm text-muted-foreground">
          Upload OPS data to look up flight plans by location
        </p>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <div className="space-y-2">
        <span className="text-xs font-medium text-muted-foreground">Location</span>
        <div className="flex gap-2">
          <Input
            value={query}
            onChange={(e) => onQueryChange(e.target.value)}
            onKeyDown={(e) => { if (e.key === 'Enter') onSearch(); }}
            placeholder="Address, city, or lat:long"
            className="h-9 text-sm"
          />
          <Button onClick={onSearch} disabled={loading || !query.trim()} size="sm" className="gap-1.5 flex-shrink-0">
            {loading ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Search className="w-3.5 h-3.5" />}
            Search
          </Button>
        </div>
        <p className="text-[11px] text-muted-foreground">
          e.g. "Iloilo City" or "10.7202, 122.5621"
        </p>
      </div>

      <div className="space-y-2">
        <span className="text-xs font-medium text-muted-foreground">Radius</span>
        <div className="flex items-center gap-2 flex-wrap">
          <div className="flex items-center gap-1.5">
            <Input
              type="number"
              min={0.1}
              step={0.1}
              value={radiusKm}
              onChange={(e) => onRadiusChange(Math.max(0.1, Number(e.target.value) || 0.1))}
              className="h-9 text-sm w-20"
            />
            <span className="text-xs text-muted-foreground">km</span>
          </div>
          <div className="flex gap-1 flex-wrap">
            {RADIUS_PRESETS_KM.map(preset => (
              <button
                key={preset}
                onClick={() => onRadiusChange(preset)}
                className={`px-2 py-1 rounded-full text-xs border transition-colors ${
                  radiusKm === preset
                    ? 'bg-primary text-primary-foreground border-primary'
                    : 'text-muted-foreground border-border hover:border-primary/50 hover:text-foreground'
                }`}
              >
                {preset}km
              </button>
            ))}
          </div>
        </div>
      </div>

      {error && (
        <div className="flex items-center gap-2 p-3 rounded-lg bg-destructive/10 border border-destructive/20">
          <AlertCircle className="w-4 h-4 text-destructive flex-shrink-0" />
          <p className="text-xs text-destructive">{error}</p>
        </div>
      )}

      {candidates && candidates.length > 0 && (
        <div className="space-y-2">
          <span className="text-xs font-medium text-muted-foreground">Multiple matches — pick one</span>
          <div className="space-y-1">
            {candidates.map((c, i) => (
              <button
                key={i}
                onClick={() => onSelectCandidate(c)}
                className="w-full text-left p-2 rounded-lg text-xs bg-muted/50 hover:bg-muted transition-colors"
              >
                {c.displayName}
              </button>
            ))}
          </div>
        </div>
      )}

      {center && (
        <>
          <div className="glass-panel rounded-lg p-4 space-y-1">
            <div className="flex items-start justify-between gap-2">
              <div className="flex items-center gap-2 min-w-0">
                <MapPin className="w-4 h-4 text-primary flex-shrink-0" />
                <h3 className="text-sm font-semibold text-foreground truncate">{center.displayName}</h3>
              </div>
              <button onClick={onClear} title="Clear search" className="text-muted-foreground hover:text-foreground flex-shrink-0">
                <X className="w-4 h-4" />
              </button>
            </div>
            <p className="font-mono text-xs text-muted-foreground">
              {center.lat.toFixed(5)}, {center.lng.toFixed(5)}
            </p>
            <p className="text-2xl font-bold text-foreground">{matchingOps.length}</p>
            <p className="text-xs text-muted-foreground">
              flight plan{matchingOps.length !== 1 ? 's' : ''} within {radiusKm}km
            </p>
          </div>

          {matchingOps.length > 0 && (
            <div className="grid grid-cols-2 gap-2">
              <Button onClick={() => onExport('json')} className="w-full gap-2" variant="outline" size="sm">
                <Download className="w-3.5 h-3.5" />
                Export JSON
              </Button>
              <Button onClick={() => onExport('xlsx')} className="w-full gap-2" variant="outline" size="sm">
                <Download className="w-3.5 h-3.5" />
                Export XLSX
              </Button>
            </div>
          )}

          <MatchedOpsList
            ops={matchingOps}
            activeOpId={activeOpId}
            hoveredOpId={hoveredOpId}
            onActivateOp={onActivateOp}
            onHoverOp={onHoverOp}
            emptyMessage="No flight plans within this radius."
          />
        </>
      )}
    </div>
  );
}
