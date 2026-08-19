import { useState, useCallback, useMemo } from 'react';
import * as turf from '@turf/turf';
import { Plane, AlertCircle, Download, CheckSquare, Square, Shield, FileText, Trash2, Crosshair } from 'lucide-react';
import { FileUpload } from '@/components/FileUpload';
import { OpsList } from '@/components/OpsList';
import { AorList } from '@/components/AorList';
import { OpsDetails } from '@/components/OpsDetails';
import { AorDetails } from '@/components/AorDetails';
import { ReportPanel } from '@/components/ReportPanel';
import { FlightLookup } from '@/components/FlightLookup';
import { FlightMap } from '@/components/FlightMap';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { parseOps, processOps, opsToGeoJSON, getOverallTimeRange, formatArea } from '@/utils/opsUtils';
import { parseAors, processAor, aorsToGeoJSON } from '@/utils/aorUtils';
import { getOpsInAor, getAorReportSummary, getOpsNearPoint } from '@/utils/reportUtils';
import { resolveLocation, type GeocodeResult } from '@/utils/geocodeUtils';
import type { ParsedOps, ParsedAor, OpsGeoJSON, ViewerGeoJSON, ViewerFeature } from '@/types/ops';
import { DateRange } from 'react-day-picker';
import * as XLSX from 'xlsx';
import { useSearchParams } from 'react-router-dom';

const LOOKUP_RADIUS_ID = '__lookup_radius__';

const Index = () => {
  const [searchParams] = useSearchParams();
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
  const [closureReasonFilter, setClosureReasonFilter] = useState<Set<string>>(new Set());
  const [reportAorId, setReportAorId] = useState<string | null>(null);
  const [opsSearchQuery, setOpsSearchQuery] = useState('');
  const [aorSearchQuery, setAorSearchQuery] = useState('');
  const [lookupQuery, setLookupQuery] = useState('');
  const [lookupRadiusKm, setLookupRadiusKm] = useState(1);
  const [lookupCenter, setLookupCenter] = useState<GeocodeResult | null>(null);
  const [lookupCandidates, setLookupCandidates] = useState<GeocodeResult[] | null>(null);
  const [lookupLoading, setLookupLoading] = useState(false);
  const [lookupError, setLookupError] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState(() => {
    const tab = searchParams.get('tab');
    return tab === 'aors' ? 'aors' : 'ops';
  });

  const handleOpsFileLoad = useCallback((data: unknown, fileName: string) => {
    try {
      setError(null);
      const parsedData = parseOps(data);
      const processedOps = parsedData.map((op, index) => processOps(op, index));
      setOps(processedOps);
      setTimeframe(getOverallTimeRange(processedOps));
      setClosureReasonFilter(new Set());
      setOpsSearchQuery('');
      setSelectedOpIds(new Set());
      setActiveId(null);
      setLookupQuery('');
      setLookupCenter(null);
      setLookupCandidates(null);
      setLookupError(null);
      setUploadedOpsFileName(fileName);
      setActiveTab('ops');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to parse ops data');
      setOps([]);
    }
  }, []);

  const handleAorFileLoad = useCallback((data: unknown, fileName: string) => {
    try {
      const { aors: parsedAors, skipped } = parseAors(data);
      const processedAors = parsedAors.map((aor, index) => processAor(aor, index));
      setError(skipped > 0
        ? `Loaded ${processedAors.length} AoR(s). ${skipped} ${skipped === 1 ? 'entry was' : 'entries were'} skipped (unrecognized format or geometry).`
        : null);
      setAors(processedAors);
      setSelectedAorIds(new Set());
      setActiveId(null);
      setReportAorId(null);
      setAorSearchQuery('');
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

  const availableClosureReasons = useMemo(() => {
    const reasons = new Set<string>();
    ops.forEach(op => {
      if (op.closureReason) reasons.add(op.closureReason);
    });
    return Array.from(reasons).sort();
  }, [ops]);

  const handleToggleClosureReason = useCallback((reason: string) => {
    setClosureReasonFilter(prev => {
      const next = new Set(prev);
      if (next.has(reason)) {
        next.delete(reason);
      } else {
        next.add(reason);
      }
      return next;
    });
    setActiveId(null);
  }, []);

  const handleClearClosureReasons = useCallback(() => {
    setClosureReasonFilter(new Set());
    setActiveId(null);
  }, []);

  const handleActivateAor = useCallback((id: string | null) => {
    setActiveId(id);
    setActiveType(id ? 'aor' : null);
    if (id) setReportAorId(id);
  }, []);

  const handleLookupSearch = useCallback(async () => {
    const trimmed = lookupQuery.trim();
    if (!trimmed) return;

    setLookupError(null);
    setLookupCandidates(null);
    setLookupLoading(true);
    try {
      const results = await resolveLocation(trimmed);
      if (results.length === 0) {
        setLookupError('No location found for that search.');
      } else if (results.length === 1) {
        setLookupCenter(results[0]);
      } else {
        setLookupCandidates(results);
      }
    } catch (e) {
      setLookupError(e instanceof Error ? e.message : 'Failed to look up that location.');
    } finally {
      setLookupLoading(false);
    }
  }, [lookupQuery]);

  const handleSelectLookupCandidate = useCallback((result: GeocodeResult) => {
    setLookupCenter(result);
    setLookupCandidates(null);
  }, []);

  const handleClearLookup = useCallback(() => {
    setLookupCenter(null);
    setLookupCandidates(null);
    setLookupError(null);
    setLookupQuery('');
  }, []);

  const filteredOps = useMemo(() => {
    let result = ops;

    if (timeframe?.from) {
      const fromDate = timeframe.from;
      const startOfDay = Date.UTC(fromDate.getFullYear(), fromDate.getMonth(), fromDate.getDate(), 0, 0, 0, 0);

      const toDate = timeframe.to || timeframe.from;
      const endOfDay = Date.UTC(toDate.getFullYear(), toDate.getMonth(), toDate.getDate(), 23, 59, 59, 999);

      result = result.filter(op => {
        const opStart = op.startTime.getTime();
        const opEnd = op.endTime.getTime();
        return opStart <= endOfDay && opEnd >= startOfDay;
      });
    }

    if (closureReasonFilter.size > 0) {
      result = result.filter(op => op.closureReason && closureReasonFilter.has(op.closureReason));
    }

    if (opsSearchQuery.trim()) {
      const query = opsSearchQuery.trim().toLowerCase();
      result = result.filter(op =>
        op.title?.toLowerCase().includes(query) ||
        op.operation_plan_id?.toLowerCase().includes(query) ||
        op.operator?.toLowerCase().includes(query) ||
        op.description?.toLowerCase().includes(query)
      );
    }

    return result;
  }, [ops, timeframe, closureReasonFilter, opsSearchQuery]);

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

  const filteredAors = useMemo(() => {
    if (!aorSearchQuery.trim()) return aors;
    const query = aorSearchQuery.trim().toLowerCase();
    return aors.filter(aor =>
      aor.name?.toLowerCase().includes(query) ||
      aor.designator?.toLowerCase().includes(query) ||
      aor.id?.toLowerCase().includes(query) ||
      aor.message?.toLowerCase().includes(query) ||
      aor.restriction?.toLowerCase().includes(query) ||
      aor.reasons?.some(r => r.toLowerCase().includes(query))
    );
  }, [aors, aorSearchQuery]);

  const visibleSelectedAorIds = useMemo(() => {
    const currentAorIds = filteredAors.map(a => a.id);
    return new Set([...selectedAorIds].filter(id => currentAorIds.includes(id)));
  }, [selectedAorIds, filteredAors]);

  const totalSelectedAorArea = useMemo(() => {
    return filteredAors.reduce((total, aor) => {
      if (visibleSelectedAorIds.has(aor.id)) {
        return total + aor.computedArea;
      }
      return total;
    }, 0);
  }, [filteredAors, visibleSelectedAorIds]);

  const activeOp = activeType === 'ops' ? filteredOps.find(p => p.operation_plan_id === activeId) : undefined;
  const activeAor = activeType === 'aor' ? aors.find(a => a.id === activeId) : undefined;

  const reportAor = aors.find(a => a.id === reportAorId);

  const reportMatchingOps = useMemo(() => {
    if (!reportAor) return [];
    return getOpsInAor(filteredOps, reportAor);
  }, [reportAor, filteredOps]);

  const opsGeojson = useMemo<OpsGeoJSON>(() => opsToGeoJSON(filteredOps), [filteredOps]);
  const aorGeojson = useMemo(() => aorsToGeoJSON(aors), [aors]);
  const filteredAorGeojson = useMemo(() => aorsToGeoJSON(filteredAors), [filteredAors]);

  const reportGeojson = useMemo<ViewerGeoJSON | null>(() => {
    if (!reportAor) return aorGeojson;
    return {
      type: 'FeatureCollection',
      features: [...aorsToGeoJSON([reportAor]).features, ...opsToGeoJSON(reportMatchingOps).features],
    };
  }, [reportAor, reportMatchingOps, aorGeojson]);

  const lookupMatchingOps = useMemo(() => {
    if (!lookupCenter) return [];
    return getOpsNearPoint(filteredOps, [lookupCenter.lng, lookupCenter.lat], lookupRadiusKm);
  }, [lookupCenter, lookupRadiusKm, filteredOps]);

  const lookupGeojson = useMemo<ViewerGeoJSON | null>(() => {
    if (!lookupCenter) return null;
    const circle = turf.circle([lookupCenter.lng, lookupCenter.lat], lookupRadiusKm, { steps: 64, units: 'kilometers' });
    const radiusFeature: ViewerFeature = {
      type: 'Feature',
      properties: {
        dataType: 'aor',
        aorId: LOOKUP_RADIUS_ID,
        name: 'Search Radius',
        designator: `${lookupRadiusKm} km`,
        lowerLimit: 0,
        upperLimit: 0,
        limitUnit: '',
        verticalReference: '',
        area: turf.area(circle),
        color: '#F43F5E',
      },
      geometry: circle.geometry,
    };
    return {
      type: 'FeatureCollection',
      features: [radiusFeature, ...opsToGeoJSON(lookupMatchingOps).features],
    };
  }, [lookupCenter, lookupRadiusKm, lookupMatchingOps]);

  const viewerGeojson = useMemo<ViewerGeoJSON | null>(() => {
    if (activeTab === 'ops') return opsGeojson;
    if (activeTab === 'aors') return filteredAorGeojson;
    if (activeTab === 'report') return reportGeojson;
    if (activeTab === 'lookup') return lookupGeojson;
    return null;
  }, [activeTab, opsGeojson, filteredAorGeojson, reportGeojson, lookupGeojson]);

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

  const handleDeleteOp = useCallback((id: string) => {
    setOps(prev => prev.filter(o => o.operation_plan_id !== id));
    setSelectedOpIds(prev => {
      if (!prev.has(id)) return prev;
      const next = new Set(prev);
      next.delete(id);
      return next;
    });
    setActiveId(prev => (prev === id ? null : prev));
  }, []);

  const handleDeleteSelectedOps = useCallback(() => {
    if (visibleSelectedOpIds.size === 0) return;
    if (!window.confirm(`Delete ${visibleSelectedOpIds.size} selected operation(s)? This cannot be undone.`)) return;
    setOps(prev => prev.filter(o => !visibleSelectedOpIds.has(o.operation_plan_id)));
    setSelectedOpIds(new Set());
    setActiveId(prev => (prev && visibleSelectedOpIds.has(prev) ? null : prev));
  }, [visibleSelectedOpIds]);

  const handleDeleteAor = useCallback((id: string) => {
    setAors(prev => prev.filter(a => a.id !== id));
    setSelectedAorIds(prev => {
      if (!prev.has(id)) return prev;
      const next = new Set(prev);
      next.delete(id);
      return next;
    });
    setActiveId(prev => (prev === id ? null : prev));
    setReportAorId(prev => (prev === id ? null : prev));
  }, []);

  const handleDeleteSelectedAors = useCallback(() => {
    if (visibleSelectedAorIds.size === 0) return;
    if (!window.confirm(`Delete ${visibleSelectedAorIds.size} selected AoR(s)? This cannot be undone.`)) return;
    setAors(prev => prev.filter(a => !visibleSelectedAorIds.has(a.id)));
    setSelectedAorIds(new Set());
    setActiveId(prev => (prev && visibleSelectedAorIds.has(prev) ? null : prev));
    setReportAorId(prev => (prev && visibleSelectedAorIds.has(prev) ? null : prev));
  }, [visibleSelectedAorIds]);

  const handleSelectAllOps = useCallback(() => {
    if (visibleSelectedOpIds.size === filteredOps.length) {
      setSelectedOpIds(new Set());
    } else {
      setSelectedOpIds(new Set(filteredOps.map(p => p.operation_plan_id)));
    }
  }, [filteredOps, visibleSelectedOpIds.size]);

  const handleSelectAllAors = useCallback(() => {
    if (visibleSelectedAorIds.size === filteredAors.length) {
      setSelectedAorIds(new Set());
    } else {
      setSelectedAorIds(new Set(filteredAors.map(a => a.id)));
    };
  }, [filteredAors, visibleSelectedAorIds.size]);

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
    if (filteredAors.length === 0 || visibleSelectedAorIds.size === 0) return;

    const selectedAorsToExport = filteredAors
      .filter(a => visibleSelectedAorIds.has(a.id))
      .map(aor => ({
        id: aor.id,
        name: aor.name,
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
  }, [filteredAors, visibleSelectedAorIds, uploadedAorFileName]);

  const handleReportExport = useCallback((format: 'json' | 'xlsx') => {
    if (aors.length === 0 || filteredOps.length === 0) return;

    const summary = getAorReportSummary(aors, filteredOps);
    const rows = summary.map(({ aor, matchCount }) => ({
      'Geozone ID': aor.designator || aor.id,
      'Name': aor.name,
      'Restriction': aor.restriction || '',
      'Plans Matched': matchCount,
    }));

    if (format === 'json') {
      const exportObject = {
        "comment": `Total number of geozones: ${rows.length}`,
        "data": rows
      };

      const blob = new Blob([JSON.stringify(exportObject, null, 2)], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `geozone-report-${new Date().toISOString().slice(0, 10)}.json`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } else if (format === 'xlsx') {
      const worksheet = XLSX.utils.json_to_sheet(rows);
      const workbook = XLSX.utils.book_new();
      XLSX.utils.book_append_sheet(workbook, worksheet, "Report");
      const xlsxBuffer = XLSX.write(workbook, { bookType: 'xlsx', type: 'array' });
      const blob = new Blob([xlsxBuffer], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `geozone-report-${new Date().toISOString().slice(0, 10)}.xlsx`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    }
  }, [aors, filteredOps]);

  const handleLookupExport = useCallback((format: 'json' | 'xlsx') => {
    if (lookupMatchingOps.length === 0 || !lookupCenter) return;

    const centerPoint: [number, number] = [lookupCenter.lng, lookupCenter.lat];
    const lookupOpsToExport = lookupMatchingOps.flatMap(p =>
      p.operation_volumes.map(v => {
        let distanceFromCenterKm: number | undefined;
        if (v.operation_geography) {
          try {
            const feature = v.operation_geography.type === 'Polygon'
              ? turf.polygon(v.operation_geography.coordinates as number[][][])
              : turf.multiPolygon(v.operation_geography.coordinates as number[][][][]);
            distanceFromCenterKm = Math.round(turf.distance(centerPoint, turf.centroid(feature), { units: 'kilometers' }) * 100) / 100;
          } catch {
            distanceFromCenterKm = undefined;
          }
        }

        return {
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
          distanceFromSearchCenterKm: distanceFromCenterKm,
          color: p.color
        };
      })
    );

    if (format === 'json') {
      const exportObject = {
        "comment": `${lookupMatchingOps.length} flight plan(s) within ${lookupRadiusKm}km of ${lookupCenter.displayName}`,
        "data": lookupOpsToExport
      };

      const blob = new Blob([JSON.stringify(exportObject, null, 2)], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `flight-lookup-${new Date().toISOString().slice(0, 10)}.json`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } else if (format === 'xlsx') {
      const worksheet = XLSX.utils.json_to_sheet(lookupOpsToExport);
      const workbook = XLSX.utils.book_new();
      XLSX.utils.book_append_sheet(workbook, worksheet, "Flight Lookup");
      const xlsxBuffer = XLSX.write(workbook, { bookType: 'xlsx', type: 'array' });
      const blob = new Blob([xlsxBuffer], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `flight-lookup-${new Date().toISOString().slice(0, 10)}.xlsx`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    }
  }, [lookupMatchingOps, lookupCenter, lookupRadiusKm]);

  const highlightedIds = useMemo(() => {
    let ids: Set<string>;
    if (activeTab === 'ops') {
      ids = new Set(visibleSelectedOpIds);
    } else if (activeTab === 'aors') {
      ids = new Set(visibleSelectedAorIds);
    } else if (activeTab === 'report') {
      ids = new Set(reportMatchingOps.map(o => o.operation_plan_id));
      if (reportAorId) ids.add(reportAorId);
    } else {
      ids = new Set(lookupMatchingOps.map(o => o.operation_plan_id));
    }
    if(hoveredId) ids.add(hoveredId);
    if(activeId) ids.add(activeId);
    return ids;
  }, [activeId, hoveredId, visibleSelectedOpIds, visibleSelectedAorIds, activeTab, reportMatchingOps, reportAorId, lookupMatchingOps]);

  return (
    <div className="flex h-screen bg-background overflow-hidden">
      <div className="w-[380px] flex-shrink-0 border-r border-border bg-sidebar flex flex-col">
        
        <div className="p-4 border-b border-sidebar-border">
          <div className="flex items-center gap-3">
            <div className="p-2 rounded-lg bg-primary/10">
              <Plane className="w-5 h-5 text-primary" />
            </div>
            <div>
              <h1 className="text-lg font-semibold text-foreground">UAS Tool</h1>
              <p className="text-xs text-muted-foreground">UAV Flight Plan Viewer</p>
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
            <TabsList className="grid w-full grid-cols-4 mx-auto sticky top-0 bg-sidebar p-4">
              <TabsTrigger value="ops" className="flex items-center gap-1 px-1.5 text-xs">
                <Plane className="w-3.5 h-3.5" />
                <span>OPS</span>
                {filteredOps.length > 0 && (
                  <Badge variant="secondary" className="px-1.5 py-0 text-[10px]">
                    {filteredOps.length}
                  </Badge>
                )}
              </TabsTrigger>
              <TabsTrigger value="aors" className="flex items-center gap-1 px-1.5 text-xs">
                <Shield className="w-3.5 h-3.5" />
                <span>AoRs</span>
                {filteredAors.length > 0 && (
                  <Badge variant="secondary" className="px-1.5 py-0 text-[10px]">{filteredAors.length}</Badge>
                )}
              </TabsTrigger>
              <TabsTrigger value="report" className="flex items-center gap-1 px-1.5 text-xs">
                <FileText className="w-3.5 h-3.5" />
                <span>Report</span>
              </TabsTrigger>
              <TabsTrigger value="lookup" className="flex items-center gap-1 px-1.5 text-xs">
                <Crosshair className="w-3.5 h-3.5" />
                <span>Lookup</span>
              </TabsTrigger>
            </TabsList>
            <TabsContent value="ops" className="flex-1 overflow-y-auto scrollbar-thin p-4 space-y-4">
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
                    onDelete={handleDeleteOp}
                    onTimeframeChange={handleTimeframeChange}
                    timeframe={timeframe}
                    availableClosureReasons={availableClosureReasons}
                    closureReasonFilter={closureReasonFilter}
                    onToggleClosureReason={handleToggleClosureReason}
                    onClearClosureReasons={handleClearClosureReasons}
                    searchQuery={opsSearchQuery}
                    onSearchChange={setOpsSearchQuery}
                  />
                </div>
              )}
            </TabsContent>
            <TabsContent value="aors" className="flex-1 overflow-y-auto scrollbar-thin p-4 space-y-4">
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
                      {visibleSelectedAorIds.size === filteredAors.length && filteredAors.length > 0 ? (
                        <CheckSquare className="w-3.5 h-3.5" />
                      ) : (
                        <Square className="w-3.5 h-3.5" />
                      )}
                      {visibleSelectedAorIds.size === filteredAors.length && filteredAors.length > 0 ? 'Deselect' : 'Select'} all
                    </button>
                  </div>
                  <AorList
                    aors={filteredAors}
                    activeAorId={activeType === 'aor' ? activeId : null}
                    selectedAorIds={visibleSelectedAorIds}
                    hoveredAorId={hoveredId}
                    onActivateAor={handleActivateAor}
                    onToggleSelect={handleToggleSelectAor}
                    onDelete={handleDeleteAor}
                    onHoverAor={setHoveredId}
                    searchQuery={aorSearchQuery}
                    onSearchChange={setAorSearchQuery}
                  />
                </div>
              )}
            </TabsContent>
            <TabsContent value="report" className="flex-1 overflow-y-auto scrollbar-thin p-4 space-y-4">
              <div>
                <h3 className="text-sm font-semibold text-foreground mb-2 flex items-center gap-2">
                  <FileText className="w-4 h-4" /> Report
                </h3>
                <p className="text-xs text-muted-foreground mb-3">
                  Select an AoR to see the flight plans within it, in the current timeframe.
                </p>
              </div>
              <ReportPanel
                aors={aors}
                hasOps={ops.length > 0}
                selectedAorId={reportAorId}
                onSelectAor={setReportAorId}
                matchingOps={reportMatchingOps}
                activeOpId={activeType === 'ops' ? activeId : null}
                hoveredOpId={hoveredId}
                onActivateOp={(id) => { setActiveId(id); setActiveType('ops'); }}
                onHoverOp={setHoveredId}
                onExport={handleReportExport}
              />
            </TabsContent>
            <TabsContent value="lookup" className="flex-1 overflow-y-auto scrollbar-thin p-4 space-y-4">
              <div>
                <h3 className="text-sm font-semibold text-foreground mb-2 flex items-center gap-2">
                  <Crosshair className="w-4 h-4" /> Flight Lookup
                </h3>
                <p className="text-xs text-muted-foreground mb-3">
                  Search an address, city, or coordinates to find flight plans within a radius.
                </p>
              </div>
              <FlightLookup
                hasOps={ops.length > 0}
                query={lookupQuery}
                onQueryChange={setLookupQuery}
                radiusKm={lookupRadiusKm}
                onRadiusChange={setLookupRadiusKm}
                onSearch={handleLookupSearch}
                loading={lookupLoading}
                error={lookupError}
                center={lookupCenter}
                candidates={lookupCandidates}
                onSelectCandidate={handleSelectLookupCandidate}
                onClear={handleClearLookup}
                matchingOps={lookupMatchingOps}
                activeOpId={activeType === 'ops' ? activeId : null}
                hoveredOpId={hoveredId}
                onActivateOp={(id) => { setActiveId(id); setActiveType('ops'); }}
                onHoverOp={setHoveredId}
                onExport={handleLookupExport}
              />
            </TabsContent>
          </Tabs>
        </div>

        {activeTab !== 'report' && activeTab !== 'lookup' && (
        <div className="border-t border-sidebar-border p-4 space-y-3 bg-sidebar-footer">
          <div className="flex items-center justify-between text-sm">
            <span className="font-semibold text-foreground">
              {activeTab === 'ops' ? 'OPS Selected' : 'AoRs Selected'}
            </span>
            <span className="text-muted-foreground">
              {activeTab === 'ops' ? `${visibleSelectedOpIds.size} / ${filteredOps.length}` : `${visibleSelectedAorIds.size} / ${filteredAors.length}`}
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
              <Button onClick={handleDeleteSelectedOps} className="w-full gap-2" variant="destructive">
                <Trash2 className="w-4 h-4" />
                Delete Selected
              </Button>
            </div>
          )}

          {visibleSelectedAorIds.size > 0 && activeTab === 'aors' && (
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
              <Button onClick={handleDeleteSelectedAors} className="w-full gap-2" variant="destructive">
                <Trash2 className="w-4 h-4" />
                Delete Selected
              </Button>
            </div>
          )}
        </div>
        )}

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
          onZoneClick={(id, dataType) => {
            if (id === LOOKUP_RADIUS_ID) return;
            if (dataType === 'aor') {
              handleActivateAor(id);
            } else {
              setActiveId(id);
              setActiveType('ops');
              if (activeTab === 'aors') {
                setActiveTab('ops');
              }
            }
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
