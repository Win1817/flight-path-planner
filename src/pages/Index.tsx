import { useState, useCallback } from 'react';
import { Plane, AlertCircle, Download, CheckSquare, Square } from 'lucide-react';
import { FileUpload } from '@/components/FileUpload';
import { FlightPlanList } from '@/components/FlightPlanList';
import { FlightDetails } from '@/components/FlightDetails';
import { FlightMap } from '@/components/FlightMap';
import { Button } from '@/components/ui/button';
import { 
  parseFlightPlans, 
  processFlightPlan, 
  flightPlansToGeoJSON 
} from '@/utils/flightPlanUtils';
import type { ParsedFlightPlan, FlightPlanGeoJSON } from '@/types/flightPlan';

const Index = () => {
  const [plans, setPlans] = useState<ParsedFlightPlan[]>([]);
  const [geojson, setGeojson] = useState<FlightPlanGeoJSON | null>(null);
  const [selectedPlanIds, setSelectedPlanIds] = useState<Set<string>>(new Set());
  const [activePlanId, setActivePlanId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [hoveredPlanId, setHoveredPlanId] = useState<string | null>(null);
  const [uploadedFileName, setUploadedFileName] = useState<string | null>(null);

  const handleFileLoad = useCallback((data: unknown) => {
    try {
      setError(null);
      const rawPlans = parseFlightPlans(data);
      const processedPlans = rawPlans.map(processFlightPlan);
      setPlans(processedPlans);
      setGeojson(flightPlansToGeoJSON(processedPlans));
      setSelectedPlanIds(new Set());
      setActivePlanId(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to parse flight plans');
      setPlans([]);
      setGeojson(null);
    }
  }, []);

  const handleError = useCallback((errorMessage: string) => {
    setError(errorMessage);
  }, []);

  const handleFileUpload = useCallback((fileName: string) => {
    setUploadedFileName(fileName);
  }, []);

  const handleToggleSelect = useCallback((planId: string) => {
    setSelectedPlanIds(prev => {
      const next = new Set(prev);
      if (next.has(planId)) {
        next.delete(planId);
      } else {
        next.add(planId);
      }
      return next;
    });
  }, []);

  const handleSelectAll = useCallback(() => {
    if (selectedPlanIds.size === plans.length) {
      setSelectedPlanIds(new Set());
    } else {
      setSelectedPlanIds(new Set(plans.map(p => p.operation_plan_id)));
    }
  }, [plans, selectedPlanIds.size]);

  const handleExport = useCallback(() => {
    if (plans.length === 0 || selectedPlanIds.size === 0) return;

    const selectedPlans = plans.filter(p => selectedPlanIds.has(p.operation_plan_id));

    const blob = new Blob([JSON.stringify(selectedPlans, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = uploadedFileName ? uploadedFileName.replace('.json', '-export.json') : `flight-plans-${new Date().toISOString().slice(0, 10)}.json`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  }, [plans, selectedPlanIds, uploadedFileName]);

  const activePlan = plans.find(p => p.operation_plan_id === activePlanId);
  const highlightedPlanIds = selectedPlanIds.size > 0 
    ? selectedPlanIds 
    : new Set(hoveredPlanId ? [hoveredPlanId] : []);

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
              <h1 className="text-lg font-semibold text-foreground">Flight Operation Viewer</h1>
              <p className="text-xs text-muted-foreground">Upload & visualize flight plans</p>
            </div>
          </div>
          
          <FileUpload onFileLoad={handleFileLoad} onError={handleError} onFileUpload={handleFileUpload} />

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
              <div className="flex items-center gap-2">
                <button
                  onClick={handleSelectAll}
                  className="text-xs text-muted-foreground hover:text-foreground transition-colors flex items-center gap-1"
                >
                  {selectedPlanIds.size === plans.length ? (
                    <CheckSquare className="w-3.5 h-3.5" />
                  ) : (
                    <Square className="w-3.5 h-3.5" />
                  )}
                  {selectedPlanIds.size === plans.length ? 'Deselect' : 'Select'} all
                </button>
              </div>
            )}
          </div>
          <FlightPlanList
            plans={plans}
            activePlanId={activePlanId}
            selectedPlanIds={selectedPlanIds}
            hoveredPlanId={hoveredPlanId}
            onActivatePlan={setActivePlanId}
            onToggleSelect={handleToggleSelect}
          />
        </div>

        {/* Export Button */}
        {selectedPlanIds.size > 0 && (
          <div className="border-t border-sidebar-border p-4">
            <Button 
              onClick={handleExport} 
              className="w-full gap-2"
              variant="default"
            >
              <Download className="w-4 h-4" />
              Export {selectedPlanIds.size} Plan{selectedPlanIds.size !== 1 ? 's' : ''} as JSON
            </Button>
          </div>
        )}

        {/* Details Panel */}
        {activePlan && (
          <div className="border-t border-sidebar-border p-4">
            <FlightDetails
              plan={activePlan}
              onClose={() => setActivePlanId(null)}
            />
          </div>
        )}
      </div>

      {/* Map Area */}
      <div className="flex-1 relative">
        <FlightMap
          geojson={geojson}
          highlightedPlanIds={highlightedPlanIds}
          onZoneClick={setActivePlanId}
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
