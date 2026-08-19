import type { ParsedAor } from '@/types/ops';
import { Shield, CheckSquare, Square, Trash2 } from 'lucide-react';
import { SearchInput } from '@/components/SearchInput';

interface AorListProps {
  aors: ParsedAor[];
  activeAorId: string | null;
  selectedAorIds: Set<string>;
  hoveredAorId: string | null;
  onActivateAor: (id: string | null) => void;
  onToggleSelect: (id: string) => void;
  onDelete: (id: string) => void;
  onHoverAor: (id: string | null) => void;
  searchQuery: string;
  onSearchChange: (query: string) => void;
}

export const AorList = ({
  aors,
  activeAorId,
  selectedAorIds,
  hoveredAorId,
  onActivateAor,
  onToggleSelect,
  onDelete,
  onHoverAor,
  searchQuery,
  onSearchChange
}: AorListProps) => {
  return (
    <div className="space-y-3">
      <SearchInput value={searchQuery} onChange={onSearchChange} placeholder="Search AoRs by name, designator, message..." />

      {aors.length === 0 && (
        <div className="text-center py-10">
          <p className="text-sm text-muted-foreground">No AoRs match the current search.</p>
        </div>
      )}

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
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2">
                  <span
                    className="w-2.5 h-2.5 rounded-full flex-shrink-0 border border-black/10"
                    style={{ backgroundColor: aor.color || '#FFD700' }}
                  />
                  <Shield className="w-4 h-4 text-primary" />
                  <h3 className="font-semibold text-sm truncate">{aor.name}</h3>
                </div>
                <p className="text-xs text-muted-foreground mt-1">{aor.designator}</p>
              </div>
              <button
                onClick={(e) => {
                  e.stopPropagation();
                  onDelete(aor.id);
                }}
                title="Delete AoR"
                className="w-5 h-5 flex-shrink-0 rounded flex items-center justify-center text-muted-foreground/70 hover:text-destructive hover:bg-destructive/10 transition-colors"
              >
                <Trash2 className="w-3.5 h-3.5" />
              </button>
            </div>
          </li>
        ))}
      </ul>
    </div>
  );
};
