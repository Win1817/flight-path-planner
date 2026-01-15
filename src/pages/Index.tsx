import { useState, useCallback, useMemo } from 'react';
import { Plane, AlertCircle, Download, CheckSquare, Square, Shield } from 'lucide-react';
import { FileUpload } from '@/components/FileUpload';
import { FlightPlanList } from '@/components/FlightPlanList';
import { AorList } from '@/components/AorList';
import { FlightDetails } from '@/components/FlightDetails';
import { AorDetails } from '@/components/AorDetails';
import { FlightMap } from '@/components/FlightMap';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { parseFlightPlans, processFlightPlan, flightPlansToGeoJSON, getOverallTimeRange, formatArea } from '@/utils/flightPlanUtils';
import { parseAors, processAor, aorsToGeoJSON } from '@/utils/aorUtils';
import type { ParsedFlightPlan, ParsedAor, FlightPlanGeoJSON } from '@/types/flightPlan';
import { DateRange } from 'react-day-picker';
import * as XLSX from 'xlsx';

const Index = () => {
  const [plans, setPlans] = useState<ParsedFlightPlan[]>([]);
  const [aors, setAors] = useState<ParsedAor[]>([]);
  const [selectedPlanIds, setSelectedPlanIds] = useState<Set<string>>(new Set());
  const [selectedAorIds, setSelectedAorIds] = useState<Set<string>>(new Set());
  const [activeId, setActiveId] = useState<string | null>(null);
  const [activeType, setActiveType] = useState<'flight-plan' | 'aor' | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [hoveredId, setHoveredId] = useState<string | null>(null);
  const [uploadedFlightPlanFileName, setUploadedFlightPlanFileName] = useState<string | null>(null);
  const [uploadedAorFileName, setUploadedAorFileName] = useState<string | null>(null);
  const [timeframe, setTimeframe] = useState<DateRange | undefined>(undefined);
  const [activeTab, setActiveTab] = useState('flight-plans');

  const handleFlightPlanFileLoad = useCallback((data: unknown, fileName: string) => {
    try {
      setError(null);
      const parsedData = parseFlightPlans(data);
      const processedPlans = parsedData.map((plan, index) => processFlightPlan(plan, index));
      setPlans(processedPlans);
      setTimeframe(getOverallTimeRange(processedPlans));
      setSelectedPlanIds(new Set());
      setActiveId(null);
      setUploadedFlightPlanFileName(fileName);
      setActiveTab('flight-plans');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to parse flight plan data');
      setPlans([]);
    }
  }, []);

  const handleAorFileLoad = useCallback((data: unknown, fileName: string) => {
    try {
      setError(null);
      const parsedAors = parseAors(data);
      const processedAors = parsedAors.map(processAor);
      setAors(processedAors);
      setSelectedAorIds(new Set());
      setActiveId(null);
      setUploadedAorFileName(fileName);
      setActiveTab('aors');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to parse AoR data');
      setAors([]);
    }
  }, []);

  const handleError = useCallback((errorMessage: string) => {
    setError(errorMessage);
  }, []);

  const handleTimeframeChange = useCallback((dateRange: DateRange | undefined) => {
    setTimeframe(dateRange);
    setActiveId(null);
  }, []);

  const filteredPlans = useMemo(() => {
    if (!timeframe || !timeframe.from) {
      return plans;
    }
    const from = new Date(timeframe.from).setHours(0, 0, 0, 0);
    const to = timeframe.to ? new Date(timeframe.to).setHours(23, 59, 59, 999) : new Date(timeframe.from).setHours(23, 59, 59, 999);
    return plans.filter(plan => {
      const planStart = plan.startTime.getTime();
      const planEnd = plan.endTime.getTime();
      return planStart <= to && planEnd >= from;
    });
  }, [plans, timeframe]);

  const visibleSelectedPlanIds = useMemo(() => {
    const currentPlanIds = filteredPlans.map(p => p.operation_plan_id);
    return new Set([...selectedPlanIds].filter(id => currentPlanIds.includes(id)));
  }, [selectedPlanIds, filteredPlans]);
  
  const totalSelectedFlightPlanArea = useMemo(() => {
    return filteredPlans.reduce((total, plan) => {
      if (visibleSelectedPlanIds.has(plan.operation_plan_id)) {
        return total + plan.computedArea;
      }
      return total;
    }, 0);
  }, [filteredPlans, visibleSelectedPlanIds]);

  const totalSelectedAorArea = useMemo(() => {
    return aors.reduce((total, aor) => {
      if (selectedAorIds.has(aor.id)) {
        return total + aor.computedArea;
      }
      return total;
    }, 0);
  }, [aors, selectedAorIds]);

  const flightPlanGeojson = useMemo<FlightPlanGeoJSON>(() => flightPlansToGeoJSON(filteredPlans), [filteredPlans]);
  const aorGeojson = useMemo(() => aorsToGeoJSON(aors), [aors]);

  const viewerGeojson = useMemo<FlightPlanGeoJSON | null>(() => {
    if (activeTab === 'flight-plans') return flightPlanGeojson;
    if (activeTab === 'aors') return aorGeojson;
    return null;
  }, [activeTab, flightPlanGeojson, aorGeojson]);

  const handleToggleSelectPlan = useCallback((id: string) => {
    setSelectedPlanIds(prev => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  }, []);

  const handleToggleSelectAor = useCallback((id: string) => {
    setSelectedAorIds(prev => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  }, []);

  const handleSelectAllPlans = useCallback(() => {
    if (visibleSelectedPlanIds.size === filteredPlans.length) {
      setSelectedPlanIds(new Set());
    } else {
      setSelectedPlanIds(new Set(filteredPlans.map(p => p.operation_plan_id)));
    }
  }, [filteredPlans, visibleSelectedPlanIds.size]);

  const handleSelectAllAors = useCallback(() => {
    if (selectedAorIds.size === aors.length) {
      setSelectedAorIds(new Set());
    } else {
      setSelectedAorIds(new Set(aors.map(a => a.id)));
    };
  }, [aors, selectedAorIds.size]);

  const handleFlightPlanExport = useCallback((format: 'json' | 'xlsx') => {
    if (filteredPlans.length === 0 || visibleSelectedPlanIds.size === 0) return;

    const selectedPlansToExport = filteredPlans
      .filter(p => visibleSelectedPlanIds.has(p.operation_plan_id))
      .flatMap(p => 
        p.operation_volumes.map(v => ({
          operationPlanId: p.operation_plan_id,
          operator: p.operator,
          title: p.title,
          state: p.state,
          closureReason: p.closureReason,
          submitTime: p.submit_time,
          updateTime: p.update_time,
          timeBegin: v.effective_time_begin,
          timeEnd: v.effective_time_end,
          actualTimeEnd: v.actual_time_end,
          minAltitudeValue: v.min_altitude?.altitude_value,
          minAltitudeType: v.min_altitude?.vertical_reference,
          minAltitudeUnit: v.min_altitude?.units_of_measure,
          maxAltitudeValue: v.max_altitude?.altitude_value,
          maxAltitudeType: v.max_altitude?.vertical_reference,
          maxAltitudeUnit: v.max_altitude?.units_of_measure,
          area_m2: p.computedArea,
          area_km2: p.computedArea / 1_000_000,
          color: p.color
        }))
      );

    if (format === 'json') {
      const exportObject = {
        "comment": `Total number of flight plans: ${selectedPlansToExport.length}`,
        "data": selectedPlansToExport
      };

      const blob = new Blob([JSON.stringify(exportObject, null, 2)], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = uploadedFlightPlanFileName ? uploadedFlightPlanFileName.replace(/\.json$/i, '-export.json') : `flight-plans-${new Date().toISOString().slice(0, 10)}.json`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } else if (format === 'xlsx') {
      const worksheet = XLSX.utils.json_to_sheet(selectedPlansToExport);
      const workbook = XLSX.utils.book_new();
      XLSX.utils.book_append_sheet(workbook, worksheet, "Flight Plans");
      const xlsxBuffer = XLSX.write(workbook, { bookType: 'xlsx', type: 'array' });
      const blob = new Blob([xlsxBuffer], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = uploadedFlightPlanFileName ? uploadedFlightPlanFileName.replace(/\.json$/i, '-export.xlsx') : `flight-plans-${new Date().toISOString().slice(0, 10)}.xlsx`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    }
  }, [filteredPlans, visibleSelectedPlanIds, uploadedFlightPlanFileName]);

  const handleAorExport = useCallback((format: 'json' | 'xlsx') => {
    if (aors.length === 0 || selectedAorIds.size === 0) return;

    const selectedAorsToExport = aors
      .filter(a => selectedAorIds.has(a.id))
      .map(aor => ({
        id: aor.id,
        name: aor.name.split(' ')[0],
        designator: aor.designator,
        lowerLimit: aor.lowerLimit,
        upperLimit: aor.upperLimit,
        verticalLimitsUom: aor.verticalLimitsUom,
        verticalReferenceType: aor.verticalReferenceType,
        autoReject: aor.autoReject,
        autoApprovalEnabled: aor.autoApprovalEnabled,
        aorEnabled: aor.aorEnabled,
        autoTakeOffClearanceEnabled: aor.autoTakeOffClearanceEnabled,
        maxSimultaneousOperationsEnabled: aor.maxSimultaneousOperationsEnabled,
        maxSimultaneousOperations: aor.maxSimultaneousOperations,
        featureType: aor.featureType,
        area_m2: aor.computedArea,
        area_km2: aor.computedArea / 1_000_000,
      }));

    if (format === 'json') {
      const exportObject = {
        "comment": `Total number of AoRs: ${selectedAorsToExport.length}`,
        "data": selectedAorsToExport
      };

      const blob = new Blob([JSON.stringify(exportObject, null, 2)], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = uploadedAorFileName ? uploadedAorFileName.replace(/\.json$/i, '-export.json') : `aors-${new Date().toISOString().slice(0, 10)}.json`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } else if (format === 'xlsx') {
      const worksheet = XLSX.utils.json_to_sheet(selectedAorsToExport);
      const workbook = XLSX.utils.book_new();
      XLSX.utils.book_append_sheet(workbook, worksheet, "AoRs");
      const xlsxBuffer = XLSX.write(workbook, { bookType: 'xlsx', type: 'array' });
      const blob = new Blob([xlsxBuffer], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = uploadedAorFileName ? uploadedAorFileName.replace(/\.json$/i, '-export.xlsx') : `aors-${new Date().toISOString().slice(0, 10)}.xlsx`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    }
  }, [aors, selectedAorIds, uploadedAorFileName]);

  const activePlan = activeType === 'flight-plan' ? filteredPlans.find(p => p.operation_plan_id === activeId) : undefined;
  const activeAor = activeType === 'aor' ? aors.find(a => a.id === activeId) : undefined;

  const highlightedIds = useMemo(() => {
    let ids: Set<string>;
    if (activeTab === 'flight-plans') {
      ids = new Set(visibleSelectedPlanIds);
    } else { // aors
      ids = new Set(selectedAorIds);
    }
    if(hoveredId) ids.add(hoveredId);
    if(activeId) ids.add(activeId);
    return ids;
  }, [activeId, hoveredId, visibleSelectedPlanIds, selectedAorIds, activeTab]);

  return (
    <div className="flex h-screen bg-background overflow-hidden">
      <div className="w-[380px] flex-shrink-0 border-r border-border bg-sidebar flex flex-col">
        
        <div className="p-4 border-b border-sidebar-border">
          <div className="flex items-center gap-3">
            <div className="p-2 rounded-lg bg-primary/10">
              <Plane className="w-5 h-5 text-primary" />
            </div>
            <div>
              <h1 className="text-lg font-semibold text-foreground">Flight Operation Viewer</h1>
              <p className="text-xs text-muted-foreground">Upload & visualize flight plans and AoRs</p>
            </div>
          </div>
          {error && (
            <div className="mt-3 flex items-center gap-2 p-3 rounded-lg bg-destructive/10 border border-destructive/20">
              <AlertCircle className="w-4 h-4 text-destructive flex-shrink-0" />
              <p className="text-xs text-destructive">{error}</p>
            </div>
          )}
        </div>

        <div className="flex-1 overflow-y-auto scrollbar-thin">
          <Tabs value={activeTab} onValueChange={setActiveTab} className="flex flex-col h-full">
            <TabsList className="grid w-full grid-cols-2 mx-auto sticky top-0 bg-sidebar p-4">
              <TabsTrigger value="flight-plans" className="flex items-center gap-2">
                <Plane className="w-4 h-4" />
                <span>Flight Plans</span>
                {filteredPlans.length > 0 && (
                  <Badge variant="secondary" className="px-2 py-0.5 text-xs">
                    {filteredPlans.length}
                  </Badge>
                )}
              </TabsTrigger>
              <TabsTrigger value="aors" className="flex items-center gap-2">
                <Shield className="w-4 h-4" />
                <span>AoRs</span>
                {aors.length > 0 && (
                  <Badge variant="secondary" className="px-2 py-0.5 text-xs">{aors.length}</Badge>
                )}
              </TabsTrigger>
            </TabsList>
            <TabsContent value="flight-plans" className="flex-1 overflow-y-auto p-4 space-y-4">
              <div>
                <h3 className="text-sm font-semibold text-foreground mb-2 flex items-center gap-2">
                  <Plane className="w-4 h-4" /> Flight Plans
                </h3>
                <FileUpload onFileLoad={handleFlightPlanFileLoad} onError={handleError} label="Upload Flight Plan JSON" />
              </div>
              {plans.length > 0 && (
                <div>
                  <div className="flex items-center justify-between mb-3">
                    <h2 className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">Flight Plans</h2>
                    <button
                      onClick={handleSelectAllPlans}
                      className="text-xs text-muted-foreground hover:text-foreground transition-colors flex items-center gap-1"
                    >
                      {visibleSelectedPlanIds.size === filteredPlans.length && filteredPlans.length > 0 ? (
                        <CheckSquare className="w-3.5 h-3.5" />
                      ) : (
                        <Square className="w-3.5 h-3.5" />
                      )}
                      {visibleSelectedPlanIds.size === filteredPlans.length && filteredPlans.length > 0 ? 'Deselect' : 'Select'} all
                    </button>
                  </div>
                  <FlightPlanList
                    plans={filteredPlans}
                    activePlanId={activeType === 'flight-plan' ? activeId : null}
                    selectedPlanIds={visibleSelectedPlanIds}
                    hoveredPlanId={hoveredId}
                    onActivatePlan={(id) => { setActiveId(id); setActiveType('flight-plan'); }}
                    onToggleSelect={handleToggleSelectPlan}
                    onTimeframeChange={handleTimeframeChange}
                    onHoverPlan={setHoveredId}
                    timeframe={timeframe}
                  />
                </div>
              )}
            </TabsContent>
            <TabsContent value="aors" className="flex-1 overflow-y-auto p-4 space-y-4">
              <div>
                <h3 className="text-sm font-semibold text-foreground mb-2 flex items-center gap-2">
                  <Shield className="w-4 h-4" /> Areas of Responsibility (AoR)
                </h3>
                <FileUpload onFileLoad={handleAorFileLoad} onError={handleError} label="Upload AoR JSON" />
              </div>
              {aors.length > 0 && (
                <div>
                  <div className="flex items-center justify-between mb-3">
                    <h2 className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">Areas of Responsibility</h2>
                    <button
                      onClick={handleSelectAllAors}
                      className="text-xs text-muted-foreground hover:text-foreground transition-colors flex items-center gap-1"
                    >
                      {selectedAorIds.size === aors.length && aors.length > 0 ? (
                        <CheckSquare className="w-3.5 h-3.5" />
                      ) : (
                        <Square className="w-3.5 h-3.5" />
                      )}
                      {selectedAorIds.size === aors.length && aors.length > 0 ? 'Deselect' : 'Select'} all
                    </button>
                  </div>
                  <AorList
                    aors={aors}
                    activeAorId={activeType === 'aor' ? activeId : null}
                    selectedAorIds={selectedAorIds}
                    hoveredAorId={hoveredId}
                    onActivateAor={(id) => { setActiveId(id); setActiveType('aor'); }}
                    onToggleSelect={handleToggleSelectAor}
                    onHoverAor={setHoveredId}
                  />
                </div>
              )}
            </TabsContent>
          </Tabs>
        </div>

        <div className="border-t border-sidebar-border p-4 space-y-3 bg-sidebar-footer">
          <div className="flex items-center justify-between text-sm">
            <span className="font-semibold text-foreground">
              {activeTab === 'flight-plans' ? 'Flight Plans Selected' : 'AoRs Selected'}
            </span>
            <span className="text-muted-foreground">
              {activeTab === 'flight-plans' ? `${visibleSelectedPlanIds.size} / ${filteredPlans.length}` : `${selectedAorIds.size} / ${aors.length}`}
            </span>
          </div>

          {visibleSelectedPlanIds.size > 0 && activeTab === 'flight-plans' && (
            <div className="border-t border-sidebar-border pt-3 space-y-3">
              <div className="flex items-center justify-between text-sm">
                <div className="flex items-center gap-2 text-muted-foreground">
                  <Plane className="w-4 h-4" />
                  <span>Total Selected Area</span>
                </div>
                <span className="font-semibold text-foreground">{formatArea(totalSelectedFlightPlanArea)}</span>
              </div>
              <div className="grid grid-cols-2 gap-2">
                <Button onClick={() => handleFlightPlanExport('json')} className="w-full gap-2" variant="outline">
                  <Download className="w-4 h-4" />
                  Export JSON
                </Button>
                <Button onClick={() => handleFlightPlanExport('xlsx')} className="w-full gap-2" variant="outline">
                  <Download className="w-4 h-4" />
                  Export XLSX
                </Button>
              </div>
            </div>
          )}

          {selectedAorIds.size > 0 && activeTab === 'aors' && (
             <div className="border-t border-sidebar-border pt-3 space-y-3">
              <div className="flex items-center justify-between text-sm">
                <div className="flex items-center gap-2 text-muted-foreground">
                  <Shield className="w-4 h-4" />
                  <span>Total Selected Area</span>
                </div>
                <span className="font-semibold text-foreground">{formatArea(totalSelectedAorArea)}</span>
              </div>
              <div className="grid grid-cols-2 gap-2">
                <Button onClick={() => handleAorExport('json')} className="w-full gap-2" variant="outline">
                  <Download className="w-4 h-4" />
                  Export JSON
                </Button>
                <Button onClick={() => handleAorExport('xlsx')} className="w-full gap-2" variant="outline">
                  <Download className="w-4 h-4" />
                  Export XLSX
                </Button>
              </div>
            </div>
          )}
        </div>

        {(activePlan || activeAor) && (
          <div className="border-t border-sidebar-border p-4">
            {activePlan && (
                <div className="space-y-4">
                    <FlightDetails plan={activePlan} onClose={() => setActiveId(null)} />
                </div>
            )}
            {activeAor && <AorDetails aor={activeAor} onClose={() => setActiveId(null)} />}
          </div>
        )}
      </div>

      <div className="flex-1 relative">
        <FlightMap
          geojson={viewerGeojson}
          highlightedPlanIds={highlightedIds}
          onZoneClick={(id) => {
            setActiveId(id);
            setActiveType('flight-plan');
            setActiveTab('flight-plans');
          }}
          onZoneHover={setHoveredId}
        />

        {plans.length > 0 && filteredPlans.length === 0 && (
           <div className="absolute inset-0 flex items-center justify-center pointer-events-none">
             <div className="text-center p-8 rounded-xl glass-panel max-w-md">
               <h2 className="text-xl font-semibold text-foreground mb-2">No Flight Plans in Timeframe</h2>
               <p className="text-sm text-muted-foreground">There are no flight plans that match the selected time range. Try adjusting the filter.</p>
             </div>
           </div>
        )}

        {plans.length === 0 && aors.length === 0 && (
          <div className="absolute inset-0 flex items-center justify-center pointer-events-none flex-col">
            <div className="p-4 rounded-full bg-primary/10 w-fit mx-auto mb-4">
              <Plane className="w-10 h-10 text-primary" />
            </div>
            <h2 className="text-xl font-semibold text-foreground mb-2">No Data Loaded</h2>
            <p className="text-sm text-muted-foreground">Upload a file to visualize flight plans or AoRs on the map.</p>
          </div>
        )}
      </div>
    </div>
  );
};

export default Index;