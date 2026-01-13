export interface Coordinate {
  longitude: number;
  latitude: number;
}

export interface OperationVolume {
  id?: string;
  ordinal?: number;
  volume_type?: string;
  near_structure?: boolean;
  effective_time_begin: string;
  effective_time_end: string;
  actual_time_end?: string;
  min_altitude?: {
    altitude_value: number;
    units_of_measure: string;
    vertical_reference?: string;
  };
  max_altitude?: {
    altitude_value: number;
    units_of_measure: string;
    vertical_reference?: string;
  };
  operation_geography?: {
    type: string;
    coordinates: number[][][] | number[][][][];
  };
  beyond_visual_line_of_sight?: boolean;
}

export interface FlightPlan {
  operation_plan_id: string;
  flight_plan_id?: string;
  operator?: string;
  title?: string;
  description?: string;
  state?: string;
  closureReason?: string; // Added based on user request
  priority?: number;
  submit_time?: string;
  update_time?: string;
  aircraft_comments?: string;
  flight_comments?: string;
  volumes_description?: string;
  airspace_authorization?: string;
  operation_volumes: OperationVolume[];
  off_nominal_volumes?: OperationVolume[];
  uas_registrations?: string[];
  contact?: {
    name?: string;
    phone?: string;
    email?: string;
  };
}

export interface ParsedFlightPlan extends FlightPlan {
  computedArea: number; // in square meters
  startTime: Date;
  endTime: Date;
  zoneCount: number;
}

export interface FlightPlanProperties {
  flightPlanId: string;
  operationPlanId: string;
  operator: string | undefined;
  title: string;
  description: string;
  state: string | undefined;
  closureReason: string | undefined;
  volumeIndex: number;
  minAltitude: number | null;
  maxAltitude: number | null;
  altitudeUnit: string;
  startTime: string;
  endTime: string;
  area: number;
  color?: string;
}

export type FlightPlanFeature = GeoJSON.Feature<GeoJSON.Polygon | GeoJSON.MultiPolygon, FlightPlanProperties>;

export type FlightPlanGeoJSON = GeoJSON.FeatureCollection<GeoJSON.Polygon | GeoJSON.MultiPolygon, FlightPlanProperties>;
