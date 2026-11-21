CREATE TABLE devices (
    device_id text PRIMARY KEY,
    name text NOT NULL,
    zero_offset numeric NOT NULL,
    calibration_factor numeric NOT NULL CHECK (calibration_factor <> 0),
    last_seen_at timestamptz
);
CREATE TABLE weight_readings (
    device_id text NOT NULL REFERENCES devices(device_id),
    sequence bigint NOT NULL CHECK (sequence >= 0),
    observed_at timestamptz NOT NULL,
    weight_grams numeric NOT NULL,
    raw_reading numeric,
    PRIMARY KEY (device_id, sequence)
);
CREATE INDEX ix_weight_readings_device_time ON weight_readings (device_id, observed_at DESC, sequence DESC);
