import type { ParsedAor } from '@/types/flightPlan';
import { Shield, CheckSquare, Square } from 'lucide-react';

interface AorListProps {
  aors: ParsedAor[];
  activeAorId: string | null;
  selectedAorIds: Set<string>;
  hoveredAorId: string | null;
  onActivateAor: (id: string | null) => void;
  onToggleSelect: (id: string) => void;
  onHoverAor: (id: string | null) => void;
}

export const AorList = ({ aors, activeAorId, selectedAorIds, hoveredAorId, onActivateAor, onToggleSelect, onHoverAor }: AorListProps) => {
  if (aors.length === 0) {
    return (
      <div className="text-center py-10">
        <p className="text-sm text-muted-foreground">No AoRs loaded.</p>
      </div>
    );
  }

  return (
    <ul className="space-y-2">
      {aors.map(aor => (
        <li 
          key={aor.id}
          className={`p-3 rounded-lg cursor-pointer transition-colors border ${
            activeAorId === aor.id 
              ? 'bg-primary/10 border-primary' 
              : hoveredAorId === aor.id 
                ? 'bg-muted border-transparent'
                : 'border-transparent hover:bg-muted'
          }`}
          onClick={() => onActivateAor(aor.id === activeAorId ? null : aor.id)}
          onMouseEnter={() => onHoverAor(aor.id)}
          onMouseLeave={() => onHoverAor(null)}
        >
          <div className="flex items-start gap-3">
            <div onClick={(e) => { e.stopPropagation(); onToggleSelect(aor.id); }} className="pt-1">
              {selectedAorIds.has(aor.id) ? <CheckSquare className="w-4 h-4 text-primary" /> : <Square className="w-4 h-4 text-muted-foreground" />}
            </div>
            <div className="flex-1">
              <div className="flex items-center gap-2">
                <Shield className="w-4 h-4 text-primary" />
                <h3 className="font-semibold text-sm truncate">{aor.name}</h3>
              </div>
              <p className="text-xs text-muted-foreground mt-1">{aor.designator}</p>
            </div>
          </div>
        </li>
      ))}
    </ul>
  );
}; 
