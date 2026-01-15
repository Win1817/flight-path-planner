import type { ParsedAor } from '@/types/flightPlan';
import { X, Shield, ArrowDown, ArrowUp, Ruler } from 'lucide-react';
import { formatArea } from '@/utils/flightPlanUtils';

interface AorDetailsProps {
  aor: ParsedAor;
  onClose: () => void;
}

export const AorDetails = ({ aor, onClose }: AorDetailsProps) => {
  return (
    <div className="-m-4">
      <div className="flex items-center justify-between p-4 border-b border-sidebar-border">
        <div className="flex items-center gap-3">
          <Shield className="w-5 h-5 text-primary flex-shrink-0" />
          <h2 className="font-semibold text-foreground truncate">{aor.name}</h2>
        </div>
        <button onClick={onClose} className="text-muted-foreground hover:text-foreground">
          <X className="w-4 h-4" />
        </button>
      </div>
      <div className="p-4 space-y-4 text-sm">
        <div className="flex justify-between items-center">
          <span className="text-muted-foreground">Designator</span>
          <span className="font-medium text-foreground">{aor.designator}</span>
        </div>
        <div className="flex justify-between items-center">
          <span className="text-muted-foreground">Area</span>
          <span className="font-medium text-foreground">{formatArea(aor.computedArea)}</span>
        </div>
        
        <div className='space-y-2'>
          <h4 className="text-xs font-semibold text-muted-foreground uppercase">Vertical Limits</h4>
          <div className="flex items-center justify-between p-2 rounded-lg bg-muted/50">
            <div className="flex items-center gap-2">
              <ArrowDown className="w-4 h-4 text-muted-foreground" />
              <span className="text-xs">Lower Limit</span>
            </div>
            <span className="font-mono text-xs font-semibold">{aor.lowerLimit} {aor.verticalLimitsUom}</span>
          </div>
          <div className="flex items-center justify-between p-2 rounded-lg bg-muted/50">
            <div className="flex items-center gap-2">
              <ArrowUp className="w-4 h-4 text-muted-foreground" />
              <span className="text-xs">Upper Limit</span>
            </div>
            <span className="font-mono text-xs font-semibold">{aor.upperLimit} {aor.verticalLimitsUom}</span>
          </div>
           <div className="flex items-center justify-between p-2 rounded-lg bg-muted/50">
            <div className="flex items-center gap-2">
              <Ruler className="w-4 h-4 text-muted-foreground" />
              <span className="text-xs">Vertical Reference</span>
            </div>
            <span className="font-mono text-xs font-semibold">{aor.verticalReferenceType}</span>
          </div>
        </div>

         <div className='space-y-2'>
          <h4 className="text-xs font-semibold text-muted-foreground uppercase">Settings</h4>
            <div className="flex items-center justify-between">
                <span className="text-xs">Auto-reject Flights</span>
                <span className={`font-semibold text-xs px-2 py-0.5 rounded-full ${aor.autoReject ? 'bg-green-500/10 text-green-600' : 'bg-red-500/10 text-red-600'}`}>
                    {aor.autoReject ? 'ON' : 'OFF'}
                </span>
            </div>
        </div>

      </div>
    </div>
  );
};
