import { Plane, Clock, Layers, MapPin, Check } from 'lucide-react';
import { cn } from '@/lib/utils';
import type { ParsedFlightPlan } from '@/types/flightPlan';
import { formatArea, formatDateTimeShort, getOperationStatus } from '@/utils/flightPlanUtils';

interface FlightPlanListProps {
  plans: ParsedFlightPlan[];
  activePlanId: string | null;
  selectedPlanIds: Set<string>;
  hoveredPlanId: string | null;
  onActivatePlan: (planId: string) => void;
  onToggleSelect: (planId: string) => void;
}

export function FlightPlanList({ 
  plans, 
  activePlanId, 
  selectedPlanIds, 
  hoveredPlanId,
  onActivatePlan, 
  onToggleSelect 
}: FlightPlanListProps) {
  if (plans.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center py-12 text-center">
        <div className="p-4 rounded-full bg-muted/50 mb-4">
          <Plane className="w-8 h-8 text-muted-foreground" />
        </div>
        <p className="text-sm text-muted-foreground">
          No flight plans loaded
        </p>
        <p className="text-xs text-muted-foreground mt-1">
          Upload a JSON file to get started
        </p>
      </div>
    );
  }

  return (
    <div className="space-y-3">
      {plans.map((plan) => {
        const status = getOperationStatus(plan.startTime, plan.endTime);
        const isActive = activePlanId === plan.operation_plan_id;
        const isSelected = selectedPlanIds.has(plan.operation_plan_id);
        const isHovered = hoveredPlanId === plan.operation_plan_id;

        return (
          <div
            key={plan.operation_plan_id}
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
                onToggleSelect(plan.operation_plan_id);
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
              onClick={() => onActivatePlan(plan.operation_plan_id)}
              className="cursor-pointer"
            >
              <div className="flex items-start justify-between gap-2 mb-3 pr-8">
                <div className="flex-1 min-w-0">
                  <h3 className="text-sm font-semibold text-foreground truncate">
                    {plan.title || 'Untitled Operation'}
                  </h3>
                  <p className="font-mono text-xs text-muted-foreground mt-0.5 truncate">
                    {plan.operation_plan_id}
                  </p>
                </div>
                <span className={cn('status-badge', status)}>
                  <span className="w-1.5 h-1.5 rounded-full bg-current" />
                  {status.charAt(0).toUpperCase() + status.slice(1)}
                </span>
              </div>

              {plan.description && (
                <p className="text-xs text-muted-foreground line-clamp-2 mb-3">
                  {plan.description}
                </p>
              )}

              <div className="grid grid-cols-2 gap-3">
                <div className="flex items-center gap-2">
                  <Clock className="w-3.5 h-3.5 text-muted-foreground" />
                  <span className="text-xs text-muted-foreground">
                    {formatDateTimeShort(plan.startTime)}
                  </span>
                </div>
                <div className="flex items-center gap-2">
                  <Layers className="w-3.5 h-3.5 text-muted-foreground" />
                  <span className="text-xs text-muted-foreground">
                    {plan.zoneCount} zone{plan.zoneCount !== 1 ? 's' : ''}
                  </span>
                </div>
                <div className="flex items-center gap-2 col-span-2">
                  <MapPin className="w-3.5 h-3.5 text-muted-foreground" />
                  <span className="text-xs text-muted-foreground">
                    {formatArea(plan.computedArea)}
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
