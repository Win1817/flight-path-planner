import { X } from 'lucide-react';
import { cn } from '@/lib/utils';

interface ClosureReasonFilterProps {
  options: string[];
  selected: Set<string>;
  onToggle: (reason: string) => void;
  onClear: () => void;
}

export function ClosureReasonFilter({ options, selected, onToggle, onClear }: ClosureReasonFilterProps) {
  if (options.length === 0) {
    return null;
  }

  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between">
        <span className="text-xs font-medium text-muted-foreground">Closure Reason</span>
        {selected.size > 0 && (
          <button
            onClick={onClear}
            className="text-xs text-muted-foreground hover:text-foreground transition-colors flex items-center gap-0.5"
          >
            <X className="w-3 h-3" />
            Clear
          </button>
        )}
      </div>
      <div className="flex flex-wrap gap-1.5">
        {options.map(reason => {
          const isSelected = selected.has(reason);
          return (
            <button
              key={reason}
              onClick={() => onToggle(reason)}
              className={cn(
                'px-2 py-0.5 rounded-full text-xs font-medium border transition-colors',
                isSelected
                  ? 'bg-primary text-primary-foreground border-primary'
                  : 'bg-transparent text-muted-foreground border-border hover:border-primary/50 hover:text-foreground'
              )}
            >
              {reason}
            </button>
          );
        })}
      </div>
    </div>
  );
}
