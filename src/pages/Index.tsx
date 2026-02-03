import { useState, useCallback, useMemo } from 'react';
import { Plane, AlertCircle, Download, CheckSquare, Square, Shield } from 'lucide-react';
import { FileUpload } from '@/components/FileUpload';
import { OpsList } from '@/components/OpsList';
import { AorList } from '@/components/AorList';
import { OpsDetails } from '@/components/OpsDetails';
import { AorDetails } from '@/components/AorDetails';
import { FlightMap } from '@/components/FlightMap';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { parseOps, processOps, opsToGeoJSON, getOverallTimeRange, formatArea } from '@/utils/opsUtils';
import { parseAors, processAor, aorsToGeoJSON } from '@/utils/aorUtils';
import type { ParsedOps, ParsedAor, OpsGeoJSON } from '@/types/ops';
import { DateRange } from 'react-day-picker';
import * as XLSX from 'xlsx';

const Index = () => {
  const [ops, setOps] = useState<ParsedOps[]>([]);
  const [aors, setAors] = useState<ParsedAor[]>([]);
  const [selectedOpIds, setSelectedOpIds] = useState<Set<string>>(new Set());
  const [selectedAorIds, setSelectedAorIds] = useState<Set<string>>(new Set());
  const [activeId, setActiveId] = useState<string | null>(null);
  const [activeType, setActiveType] = useState<'ops' | 'aor' | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [hoveredId, setHoveredId] = useState<string | null>(null);
  const [uploadedOpsFileName, setUploadedOpsFileName] = useState<string | null>(null);
  const [uploadedAorFileName, setUploadedAorFileName] = useState<string | null>(null);
  const [timeframe, setTimeframe] = useState<DateRange | undefined>(undefined);
  const [activeTab, setActiveTab] = useState('ops');

  const handleOpsFileLoad = useCallback((data: unknown, fileName: string) => {
    try {
      setError(null);
      const parsedData = parseOps(data);
      const processedOps = parsedData.map((op, index) => processOps(op, index));
      setOps(processedOps);
      setTimeframe(getOverallTimeRange(processedOps));
      setSelectedOpIds(new Set());
      setActiveId(null);
      setUploadedOpsFileName(fileName);
      setActiveTab('ops');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to parse ops data');
      setOps([]);
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

  const filteredOps = useMemo(() => {
    if (!timeframe || !timeframe.from) {
      return ops;
    }

    const fromDate = timeframe.from;
    const startOfDay = Date.UTC(fromDate.getFullYear(), fromDate.getMonth(), fromDate.getDate(), 0, 0, 0, 0);

    const toDate = timeframe.to || timeframe.from;
    const endOfDay = Date.UTC(toDate.getFullYear(), toDate.getMonth(), toDate.getDate(), 23, 59, 59, 999);

    return ops.filter(op => {
      const opStart = op.startTime.getTime();
      const opEnd = op.endTime.getTime();
      return opStart <= endOfDay && opEnd >= startOfDay;
    });
  }, [ops, timeframe]);

  const visibleSelectedOpIds = useMemo(() => {
    const currentOpIds = filteredOps.map(p => p.operation_plan_id);
    return new Set([...selectedOpIds].filter(id => currentOpIds.includes(id)));
  }, [selectedOpIds, filteredOps]);
  
  const totalSelectedOpsArea = useMemo(() => {
    return filteredOps.reduce((total, op) => {
      if (visibleSelectedOpIds.has(op.operation_plan_id)) {
        return total + op.computedArea;
      }
      return total;
    }, 0);
  }, [filteredOps, visibleSelectedOpIds]);

  const totalSelectedAorArea = useMemo(() => {
    return aors.reduce((total, aor) => {
      if (selectedAorIds.has(aor.id)) {
        return total + aor.computedArea;
      }
      return total;
    }, 0);
  }, [aors, selectedAorIds]);

  const opsGeojson = useMemo<OpsGeoJSON>(() => opsToGeoJSON(filteredOps), [filteredOps]);
  const aorGeojson = useMemo(() => aorsToGeoJSON(aors), [aors]);

  const viewerGeojson = useMemo<OpsGeoJSON | null>(() => {
    if (activeTab === 'ops') return opsGeojson;
    if (activeTab === 'aors') return aorGeojson;
    return null;
  }, [activeTab, opsGeojson, aorGeojson]);

  const handleToggleSelectOp = useCallback((id: string) => {
    setSelectedOpIds(prev => {
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

  const handleSelectAllOps = useCallback(() => {
    if (visibleSelectedOpIds.size === filteredOps.length) {
      setSelectedOpIds(new Set());
    } else {
      setSelectedOpIds(new Set(filteredOps.map(p => p.operation_plan_id)));
    }
  }, [filteredOps, visibleSelectedOpIds.size]);

  const handleSelectAllAors = useCallback(() => {
    if (selectedAorIds.size === aors.length) {
      setSelectedAorIds(new Set());
    } else {
      setSelectedAorIds(new Set(aors.map(a => a.id)));
    };
  }, [aors, selectedAorIds.size]);

  const handleOpsExport = useCallback((format: 'json' | 'xlsx') => {
    if (filteredOps.length === 0 || visibleSelectedOpIds.size === 0) return;

    const selectedOpsToExport = filteredOps
      .filter(p => visibleSelectedOpIds.has(p.operation_plan_id))
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
        "comment": `Total number of ops: ${selectedOpsToExport.length}`,
        "data": selectedOpsToExport
      };

      const blob = new Blob([JSON.stringify(exportObject, null, 2)], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = uploadedOpsFileName ? uploadedOpsFileName.replace(/\.json$/i, '-export.json') : `ops-${new Date().toISOString().slice(0, 10)}.json`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } else if (format === 'xlsx') {
      const worksheet = XLSX.utils.json_to_sheet(selectedOpsToExport);
      const workbook = XLSX.utils.book_new();
      XLSX.utils.book_append_sheet(workbook, worksheet, "OPS");
      const xlsxBuffer = XLSX.write(workbook, { bookType: 'xlsx', type: 'array' });
      const blob = new Blob([xlsxBuffer], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = uploadedOpsFileName ? uploadedOpsFileName.replace(/\.json$/i, '-export.xlsx') : `ops-${new Date().toISOString().slice(0, 10)}.xlsx`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    }
  }, [filteredOps, visibleSelectedOpIds, uploadedOpsFileName]);

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

  const activeOp = activeType === 'ops' ? filteredOps.find(p => p.operation_plan_id === activeId) : undefined;
  const activeAor = activeType === 'aor' ? aors.find(a => a.id === activeId) : undefined;

  const highlightedIds = useMemo(() => {
    let ids: Set<string>;
    if (activeTab === 'ops') {
      ids = new Set(visibleSelectedOpIds);
    } else { // aors
      ids = new Set(selectedAorIds);
    }
    if(hoveredId) ids.add(hoveredId);
    if(activeId) ids.add(activeId);
    return ids;
  }, [activeId, hoveredId, visibleSelectedOpIds, selectedAorIds, activeTab]);

  return (
    <div className="flex h-screen bg-background overflow-hidden">
      <div className="w-[380px] flex-shrink-0 border-r border-border bg-sidebar flex flex-col">
        
        <div className="p-4 border-b border-sidebar-border">
          <div className="flex items-center gap-3">
            <div className="p-2 rounded-lg bg-primary/10">
              <Plane className="w-5 h-5 text-primary" />
            </div>
            <div>
              <h1 className="text-lg font-semibold text-foreground">OPS Viewer</h1>
              <p className="text-xs text-muted-foreground">Upload & visualize flight operations and AoRs</p>
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
              <TabsTrigger value="ops" className="flex items-center gap-2">
                <Plane className="w-4 h-4" />
                <span>OPS</span>
                {filteredOps.length > 0 && (
                  <Badge variant="secondary" className="px-2 py-0.5 text-xs">
                    {filteredOps.length}
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
            <TabsContent value="ops" className="flex-1 overflow-y-auto p-4 space-y-4">
              <div>
                <h3 className="text-sm font-semibold text-foreground mb-2 flex items-center gap-2">
                  <Plane className="w-4 h-4" /> OPS
                </h3>
                <FileUpload onFileLoad={handleOpsFileLoad} onError={handleError} label="Upload OPS JSON" />
              </div>
              {ops.length > 0 && (
                <div>
                  <div className="flex items-center justify-between mb-3">
                    <h2 className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">OPS</h2>
                    <button
                      onClick={handleSelectAllOps}
                      className="text-xs text-muted-foreground hover:text-foreground transition-colors flex items-center gap-1"
                    >
                      {visibleSelectedOpIds.size === filteredOps.length && filteredOps.length > 0 ? (
                        <CheckSquare className="w-3.5 h-3.5" />
                      ) : (
                        <Square className="w-3.5 h-3.5" />
                      )}
                      {visibleSelectedOpIds.size === filteredOps.length && filteredOps.length > 0 ? 'Deselect' : 'Select'} all
                    </button>
                  </div>
                  <OpsList
                    ops={filteredOps}
                    activeOpId={activeType === 'ops' ? activeId : null}
                    selectedOpIds={visibleSelectedOpIds}
                    hoveredOpId={hoveredId}
                    onActivateOp={(id) => { setActiveId(id); setActiveType('ops'); }}
                    onToggleSelect={handleToggleSelectOp}
                    onTimeframeChange={handleTimeframeChange}
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
              {activeTab === 'ops' ? 'OPS Selected' : 'AoRs Selected'}
            </span>
            <span className="text-muted-foreground">
              {activeTab === 'ops' ? `${visibleSelectedOpIds.size} / ${filteredOps.length}` : `${selectedAorIds.size} / ${aors.length}`}
            </span>
          </div>

          {visibleSelectedOpIds.size > 0 && activeTab === 'ops' && (
            <div className="border-t border-sidebar-border pt-3 space-y-3">
              <div className="flex items-center justify-between text-sm">
                <div className="flex items-center gap-2 text-muted-foreground">
                  <Plane className="w-4 h-4" />
                  <span>Total Selected Area</span>
                </div>
                <span className="font-semibold text-foreground">{formatArea(totalSelectedOpsArea)}</span>
              </div>
              <div className="grid grid-cols-2 gap-2">
                <Button onClick={() => handleOpsExport('json')} className="w-full gap-2" variant="outline">
                  <Download className="w-4 h-4" />
                  Export JSON
                </Button>
                <Button onClick={() => handleOpsExport('xlsx')} className="w-full gap-2" variant="outline">
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

        {(activeOp || activeAor) && (
          <div className="border-t border-sidebar-border p-4">
            {activeOp && (
                <div className="space-y-4">
                    <OpsDetails op={activeOp} onClose={() => setActiveId(null)} />
                </div>
            )}
            {activeAor && <AorDetails aor={activeAor} onClose={() => setActiveId(null)} />}
          </div>
        )}
      </div>

      <div className="flex-1 relative">
        <FlightMap
          geojson={viewerGeojson}
          highlightedIds={highlightedIds}
          onZoneClick={(id) => {
            setActiveId(id);
            setActiveType('ops');
            setActiveTab('ops');
          }}
          onZoneHover={setHoveredId}
        />

        {ops.length > 0 && filteredOps.length === 0 && (
           <div className="absolute inset-0 flex items-center justify-center pointer-events-none">
             <div className="text-center p-8 rounded-xl glass-panel max-w-md">
               <h2 className="text-xl font-semibold text-foreground mb-2">No OPS in Timeframe</h2>
               <p className="text-sm text-muted-foreground">There are no flight operations that match the selected time range. Try adjusting the filter.</p>
             </div>
           </div>
        )}

        {ops.length === 0 && aors.length === 0 && (
          <div className="absolute inset-0 flex items-center justify-center pointer-events-none flex-col">
            <div className="p-4 rounded-full bg-primary/10 w-fit mx-auto mb-4">
              <Plane className="w-10 h-10 text-primary" />
            </div>
            <h2 className="text-xl font-semibold text-foreground mb-2">No Data Loaded</h2>
            <p className="text-sm text-muted-foreground">Upload a file to visualize flight operations or AoRs on the map.</p>
          </div>
        )}
      </div>
    </div>
  );
};

export default Index;
