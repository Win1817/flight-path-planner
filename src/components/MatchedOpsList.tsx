import { Clock, MapPin } from 'lucide-react';
import { cn } from '@/lib/utils';
import type { ParsedOps } from '@/types/ops';
import { formatArea, formatDateTimeShort, getOperationStatus } from '@/utils/opsUtils';

interface MatchedOpsListProps {
  ops: ParsedOps[];
  activeOpId: string | null;
  hoveredOpId: string | null;
  onActivateOp: (id: string) => void;
  onHoverOp: (id: string | null) => void;
  emptyMessage: string;
}

export function MatchedOpsList({ ops, activeOpId, hoveredOpId, onActivateOp, onHoverOp, emptyMessage }: MatchedOpsListProps) {
  if (ops.length === 0) {
    return (
      <p className="text-xs text-muted-foreground text-center py-6">
        {emptyMessage}
      </p>
    );
  }

  return (
    <div className="space-y-2">
      {ops.map(op => {
        const status = getOperationStatus(op.startTime, op.endTime);
        const isActive = activeOpId === op.operation_plan_id;
        const isHovered = hoveredOpId === op.operation_plan_id;
        return (
          <div
            key={op.operation_plan_id}
            onClick={() => onActivateOp(op.operation_plan_id)}
            onMouseEnter={() => onHoverOp(op.operation_plan_id)}
            onMouseLeave={() => onHoverOp(null)}
            className={cn(
              'flight-card',
              isActive && 'active',
              isHovered && !isActive && 'border-primary/30'
            )}
          >
            <div className="flex items-start justify-between gap-2 mb-2">
              <div className="flex-1 min-w-0">
                <h4 className="text-sm font-semibold text-foreground truncate">
                  {op.title || 'Untitled Operation'}
                </h4>
                <p className="font-mono text-xs text-muted-foreground truncate">
                  {op.operation_plan_id}
                </p>
              </div>
              <span className={cn('status-badge', status)}>
                <span className="w-1.5 h-1.5 rounded-full bg-current" />
                {status.charAt(0).toUpperCase() + status.slice(1)}
              </span>
            </div>
            <div className="flex items-center gap-3 text-xs text-muted-foreground">
              <span className="flex items-center gap-1">
                <Clock className="w-3.5 h-3.5" />
                {formatDateTimeShort(op.startTime)}
              </span>
              <span className="flex items-center gap-1">
                <MapPin className="w-3.5 h-3.5" />
                {formatArea(op.computedArea)}
              </span>
            </div>
          </div>
        );
      })}
    </div>
  );
}
