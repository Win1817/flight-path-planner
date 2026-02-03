import { Plane, Clock, Layers, MapPin, Check } from 'lucide-react';
import { cn } from '@/lib/utils';
import type { ParsedOps } from '@/types/ops';
import { formatArea, formatDateTimeShort, getOperationStatus } from '@/utils/opsUtils';
import { TimeframeFilter } from '@/components/TimeframeFilter';
import { DateRange } from 'react-day-picker';

interface OpsListProps {
  ops: ParsedOps[];
  activeOpId: string | null;
  selectedOpIds: Set<string>;
  hoveredOpId: string | null;
  onActivateOp: (opId: string) => void;
  onToggleSelect: (opId: string) => void;
  onTimeframeChange: (dateRange: DateRange | undefined) => void;
  timeframe: DateRange | undefined;
}

export function OpsList({ 
  ops, 
  activeOpId, 
  selectedOpIds, 
  hoveredOpId,
  onActivateOp, 
  onToggleSelect,
  onTimeframeChange,
  timeframe
}: OpsListProps) {
  if (ops.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center py-12 text-center">
        <div className="p-4 rounded-full bg-muted/50 mb-4">
          <Plane className="w-8 h-8 text-muted-foreground" />
        </div>
        <p className="text-sm text-muted-foreground">
          No OPS loaded
        </p>
        <p className="text-xs text-muted-foreground mt-1">
          Upload a JSON file to get started
        </p>
      </div>
    );
  }

  return (
    <div className="space-y-3">
      <TimeframeFilter date={timeframe} onTimeframeChange={onTimeframeChange} />
      {ops.map((op) => {
        const status = getOperationStatus(op.startTime, op.endTime);
        const isActive = activeOpId === op.operation_plan_id;
        const isSelected = selectedOpIds.has(op.operation_plan_id);
        const isHovered = hoveredOpId === op.operation_plan_id;

        return (
          <div
            key={op.operation_plan_id}
            className={cn(
              'flight-card relative',
              isActive && 'active',
              isHovered && !isActive && 'border-primary/30',
              isSelected && 'ring-2 ring-primary/50'
            )}
          >
            {/* Checkbox */}
            <button
              onClick={(e) => {
                e.stopPropagation();
                onToggleSelect(op.operation_plan_id);
              }}
              className={cn(
                'absolute top-3 right-3 w-5 h-5 rounded border-2 flex items-center justify-center transition-all',
                isSelected 
                  ? 'bg-primary border-primary text-primary-foreground' 
                  : 'border-muted-foreground/50 hover:border-primary'
              )}
            >
              {isSelected && <Check className="w-3 h-3" />}
            </button>

            {/* Card content */}
            <div 
              onClick={() => onActivateOp(op.operation_plan_id)}
              className="cursor-pointer"
            >
              <div className="flex items-start justify-between gap-2 mb-3 pr-8">
                <div className="flex-1 min-w-0">
                  <h3 className="text-sm font-semibold text-foreground truncate">
                    {op.title || 'Untitled Operation'}
                  </h3>
                  <p className="font-mono text-xs text-muted-foreground mt-0.5 truncate">
                    {op.operation_plan_id}
                  </p>
                </div>
                <span className={cn('status-badge', status)}>
                  <span className="w-1.5 h-1.5 rounded-full bg-current" />
                  {status.charAt(0).toUpperCase() + status.slice(1)}
                </span>
              </div>

              {op.description && (
                <p className="text-xs text-muted-foreground line-clamp-2 mb-3">
                  {op.description}
                </p>
              )}

              <div className="grid grid-cols-2 gap-3">
                <div className="flex items-center gap-2">
                  <Clock className="w-3.5 h-3.5 text-muted-foreground" />
                  <span className="text-xs text-muted-foreground">
                    {formatDateTimeShort(op.startTime)}
                  </span>
                </div>
                <div className="flex items-center gap-2">
                  <Layers className="w-3.5 h-3.5 text-muted-foreground" />
                  <span className="text-xs text-muted-foreground">
                    {op.zoneCount} zone{op.zoneCount !== 1 ? 's' : ''}
                  </span>
                </div>
                <div className="flex items-center gap-2 col-span-2">
                  <MapPin className="w-3.5 h-3.5 text-muted-foreground" />
                  <span className="text-xs text-muted-foreground">
                    {formatArea(op.computedArea)}
                  </span>
                </div>
              </div>
            </div>
          </div>
        );
      })}
    </div>
  );
}
