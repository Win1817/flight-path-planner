import { X, Clock, Mountain, Maximize2, Calendar, FileText, Hash, Info, User } from 'lucide-react';
import type { ParsedFlightPlan } from '@/types/flightPlan';
import { formatArea, formatDateTime, getOperationStatus } from '@/utils/flightPlanUtils';
import { cn } from '@/lib/utils';

interface FlightDetailsProps {
  plan: ParsedFlightPlan;
  onClose: () => void;
}

export function FlightDetails({ plan, onClose }: FlightDetailsProps) {
  const status = getOperationStatus(plan.startTime, plan.endTime);
  const allVolumes = [...(plan.operation_volumes || []), ...(plan.off_nominal_volumes || [])];
  
  // Get min/max altitudes across all volumes
  const altitudes = allVolumes
    .filter(v => v.min_altitude || v.max_altitude)
    .map(v => ({
      min: v.min_altitude?.altitude_value,
      max: v.max_altitude?.altitude_value,
      unit: v.max_altitude?.units_of_measure || v.min_altitude?.units_of_measure || 'FT'
    }));

  const minAlt = Math.min(...altitudes.filter(a => a.min !== undefined).map(a => a.min!));
  const maxAlt = Math.max(...altitudes.filter(a => a.max !== undefined).map(a => a.max!));
  const altUnit = altitudes[0]?.unit || 'FT';

  return (
    <div className="glass-panel rounded-lg overflow-hidden">
      <div className="flex items-center justify-between p-4 border-b border-border">
        <h2 className="text-sm font-semibold text-foreground">Flight Plan Details</h2>
        <button
          onClick={onClose}
          className="p-1.5 rounded-md hover:bg-muted transition-colors"
        >
          <X className="w-4 h-4 text-muted-foreground" />
        </button>
      </div>

      <div className="p-4 space-y-5 max-h-[calc(100vh-300px)] overflow-y-auto scrollbar-thin">
        {/* Header */}
        <div>
          <div className="flex items-center gap-2 mb-2">
            <span className={cn('status-badge', status)}>
              <span className="w-1.5 h-1.5 rounded-full bg-current" />
              {status.charAt(0).toUpperCase() + status.slice(1)}
            </span>
          </div>
          <h3 className="text-lg font-semibold text-foreground">
            {plan.title || 'Untitled Operation'}
          </h3>
          {plan.description && (
            <p className="text-sm text-muted-foreground mt-2">
              {plan.description}
            </p>
          )}
        </div>

        {/* Status Information */}
        {(plan.state || plan.closureReason) && (
          <div className="space-y-3">
            <h4 className="text-xs font-semibold text-foreground uppercase tracking-wider flex items-center gap-2">
              <Info className="w-3.5 h-3.5 text-primary" />
              Status
            </h4>
            <div className="grid grid-cols-2 gap-3 bg-muted/30 rounded-lg p-3">
              {plan.state && (
                <div>
                  <p className="data-label">State</p>
                  <p className="text-md font-semibold text-foreground">
                    {plan.state}
                  </p>
                </div>
              )}
              {plan.closureReason && (
                <div>
                  <p className="data-label">Closure Reason</p>
                  <p className="text-md font-semibold text-foreground">
                    {plan.closureReason}
                  </p>
                </div>
              )}
            </div>
          </div>
        )}
        
        {/* Operator Information */}
        {plan.operator && (
          <div className="space-y-3">
            <h4 className="text-xs font-semibold text-foreground uppercase tracking-wider flex items-center gap-2">
              <User className="w-3.5 h-3.5 text-primary" />
              Operator
            </h4>
            <div className="grid grid-cols-1 gap-3 bg-muted/30 rounded-lg p-3">
              <div>
                <p className="data-label">Operator ID</p>
                <p className="font-mono text-sm text-foreground break-all">
                  {plan.operator}
                </p>
              </div>
            </div>
          </div>
        )}

        {/* IDs */}
        <div className="space-y-3">
          <div className="flex items-start gap-3">
            <Hash className="w-4 h-4 text-primary mt-0.5" />
            <div>
              <p className="data-label">Operation Plan ID</p>
              <p className="font-mono text-sm text-foreground break-all">
                {plan.operation_plan_id}
              </p>
            </div>
          </div>
          {plan.flight_plan_id && plan.flight_plan_id !== plan.operation_plan_id && (
            <div className="flex items-start gap-3">
              <FileText className="w-4 h-4 text-primary mt-0.5" />
              <div>
                <p className="data-label">Flight Plan ID</p>
                <p className="font-mono text-sm text-foreground break-all">
                  {plan.flight_plan_id}
                </p>
              </div>
            </div>
          )}
        </div>

        {/* Time Information */}
        <div className="space-y-3">
          <h4 className="text-xs font-semibold text-foreground uppercase tracking-wider flex items-center gap-2">
            <Clock className="w-3.5 h-3.5 text-primary" />
            Time Information (UTC)
          </h4>
          <div className="grid gap-3 bg-muted/30 rounded-lg p-3">
            <div>
              <p className="data-label">Start Time</p>
              <p className="font-mono text-sm text-foreground">
                {formatDateTime(plan.startTime)}
              </p>
            </div>
            <div>
              <p className="data-label">End Time</p>
              <p className="font-mono text-sm text-foreground">
                {formatDateTime(plan.endTime)}
              </p>
            </div>
            {plan.submit_time && (
              <div>
                <p className="data-label">Submit Time</p>
                <p className="font-mono text-sm text-muted-foreground">
                  {formatDateTime(plan.submit_time)}
                </p>
              </div>
            )}
            {plan.update_time && (
              <div>
                <p className="data-label">Last Updated</p>
                <p className="font-mono text-sm text-muted-foreground">
                  {formatDateTime(plan.update_time)}
                </p>
              </div>
            )}
          </div>
        </div>

        {/* Altitude Information */}
        {altitudes.length > 0 && (
          <div className="space-y-3">
            <h4 className="text-xs font-semibold text-foreground uppercase tracking-wider flex items-center gap-2">
              <Mountain className="w-3.5 h-3.5 text-primary" />
              Altitude
            </h4>
            <div className="grid grid-cols-2 gap-3 bg-muted/30 rounded-lg p-3">
              <div>
                <p className="data-label">Min Altitude</p>
                <p className="text-lg font-semibold text-foreground">
                  {isFinite(minAlt) ? minAlt : '—'}
                  <span className="text-sm text-muted-foreground ml-1">{altUnit}</span>
                </p>
              </div>
              <div>
                <p className="data-label">Max Altitude</p>
                <p className="text-lg font-semibold text-foreground">
                  {isFinite(maxAlt) ? maxAlt : '—'}
                  <span className="text-sm text-muted-foreground ml-1">{altUnit}</span>
                </p>
              </div>
            </div>
          </div>
        )}

        {/* Area Information */}
        <div className="space-y-3">
          <h4 className="text-xs font-semibold text-foreground uppercase tracking-wider flex items-center gap-2">
            <Maximize2 className="w-3.5 h-3.5 text-primary" />
            Area Information
          </h4>
          <div className="grid grid-cols-2 gap-3 bg-muted/30 rounded-lg p-3">
            <div>
              <p className="data-label">Total Area</p>
              <p className="text-lg font-semibold text-foreground">
                {formatArea(plan.computedArea)}
              </p>
            </div>
            <div>
              <p className="data-label">Zone Count</p>
              <p className="text-lg font-semibold text-foreground">
                {plan.zoneCount}
              </p>
            </div>
            <div className="col-span-2">
              <p className="data-label">Area (m²)</p>
              <p className="font-mono text-sm text-muted-foreground">
                {plan.computedArea.toLocaleString(undefined, { maximumFractionDigits: 2 })} m²
              </p>
            </div>
          </div>
        </div>

        {/* Volumes List */}
        {allVolumes.length > 1 && (
          <div className="space-y-3">
            <h4 className="text-xs font-semibold text-foreground uppercase tracking-wider flex items-center gap-2">
              <Calendar className="w-3.5 h-3.5 text-primary" />
              Operation Volumes ({allVolumes.length})
            </h4>
            <div className="space-y-2">
              {allVolumes.map((volume, index) => (
                <div key={volume.id || index} className="bg-muted/30 rounded-lg p-3">
                  <div className="flex items-center justify-between mb-2">
                    <span className="text-sm font-medium text-foreground">
                      Volume {index + 1}
                    </span>
                    {volume.id && (
                      <span className="font-mono text-xs text-muted-foreground">
                        {volume.id}
                      </span>
                    )}
                  </div>
                  <div className="grid grid-cols-2 gap-2 text-xs">
                    <div>
                      <span className="text-muted-foreground">Alt: </span>
                      <span className="text-foreground">
                        {volume.min_altitude?.altitude_value ?? '—'} - {volume.max_altitude?.altitude_value ?? '—'} {volume.max_altitude?.units_of_measure || 'FT'}
                      </span>
                    </div>
                    <div>
                      <span className="text-muted-foreground">Time: </span>
                      <span className="text-foreground">
                        {new Date(volume.effective_time_begin).toLocaleTimeString()} - {new Date(volume.effective_time_end).toLocaleTimeString()}
                      </span>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}