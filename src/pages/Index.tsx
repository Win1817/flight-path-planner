import { useState, useCallback } from 'react';
import { Plane, AlertCircle } from 'lucide-react';
import { FileUpload } from '@/components/FileUpload';
import { FlightPlanList } from '@/components/FlightPlanList';
import { FlightDetails } from '@/components/FlightDetails';
import { FlightMap } from '@/components/FlightMap';
import { 
  parseFlightPlans, 
  processFlightPlan, 
  flightPlansToGeoJSON 
} from '@/utils/flightPlanUtils';
import type { ParsedFlightPlan, FlightPlanGeoJSON } from '@/types/flightPlan';

const Index = () => {
  const [plans, setPlans] = useState<ParsedFlightPlan[]>([]);
  const [geojson, setGeojson] = useState<FlightPlanGeoJSON | null>(null);
  const [selectedPlanId, setSelectedPlanId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [hoveredPlanId, setHoveredPlanId] = useState<string | null>(null);

  const handleFileLoad = useCallback((data: unknown) => {
    try {
      setError(null);
      const rawPlans = parseFlightPlans(data);
      const processedPlans = rawPlans.map(processFlightPlan);
      setPlans(processedPlans);
      setGeojson(flightPlansToGeoJSON(processedPlans));
      
      // Auto-select first plan
      if (processedPlans.length > 0) {
        setSelectedPlanId(processedPlans[0].operation_plan_id);
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to parse flight plans');
      setPlans([]);
      setGeojson(null);
    }
  }, []);

  const handleError = useCallback((errorMessage: string) => {
    setError(errorMessage);
  }, []);

  const selectedPlan = plans.find(p => p.operation_plan_id === selectedPlanId);

  return (
    <div className="flex h-screen bg-background overflow-hidden">
      {/* Left Sidebar */}
      <div className="w-[380px] flex-shrink-0 border-r border-border bg-sidebar flex flex-col">
        {/* Header */}
        <div className="p-4 border-b border-sidebar-border">
          <div className="flex items-center gap-3 mb-4">
            <div className="p-2 rounded-lg bg-primary/10">
              <Plane className="w-5 h-5 text-primary" />
            </div>
            <div>
              <h1 className="text-lg font-semibold text-foreground">Flight Ops Viewer</h1>
              <p className="text-xs text-muted-foreground">Upload & visualize flight plans</p>
            </div>
          </div>
          
          <FileUpload onFileLoad={handleFileLoad} onError={handleError} />

          {error && (
            <div className="mt-3 flex items-center gap-2 p-3 rounded-lg bg-destructive/10 border border-destructive/20">
              <AlertCircle className="w-4 h-4 text-destructive flex-shrink-0" />
              <p className="text-xs text-destructive">{error}</p>
            </div>
          )}
        </div>

        {/* Flight Plan List */}
        <div className="flex-1 overflow-y-auto scrollbar-thin p-4">
          <div className="flex items-center justify-between mb-3">
            <h2 className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">
              Flight Plans
            </h2>
            {plans.length > 0 && (
              <span className="text-xs text-muted-foreground">
                {plans.length} plan{plans.length !== 1 ? 's' : ''}
              </span>
            )}
          </div>
          <FlightPlanList
            plans={plans}
            selectedPlanId={selectedPlanId || hoveredPlanId}
            onSelectPlan={setSelectedPlanId}
          />
        </div>

        {/* Details Panel */}
        {selectedPlan && (
          <div className="border-t border-sidebar-border p-4">
            <FlightDetails
              plan={selectedPlan}
              onClose={() => setSelectedPlanId(null)}
            />
          </div>
        )}
      </div>

      {/* Map Area */}
      <div className="flex-1 relative">
        <FlightMap
          geojson={geojson}
          selectedPlanId={selectedPlanId || hoveredPlanId}
          onZoneClick={setSelectedPlanId}
          onZoneHover={setHoveredPlanId}
        />

        {/* Empty State Overlay */}
        {!geojson && (
          <div className="absolute inset-0 flex items-center justify-center pointer-events-none">
            <div className="text-center p-8 rounded-xl glass-panel max-w-md">
              <div className="p-4 rounded-full bg-primary/10 w-fit mx-auto mb-4">
                <Plane className="w-10 h-10 text-primary" />
              </div>
              <h2 className="text-xl font-semibold text-foreground mb-2">
                No Flight Plans Loaded
              </h2>
              <p className="text-sm text-muted-foreground">
                Upload a JSON file containing flight plan data to visualize operation zones on the map.
              </p>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};

export default Index;
