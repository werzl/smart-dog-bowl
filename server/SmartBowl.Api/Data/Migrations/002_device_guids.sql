CREATE TEMP TABLE device_guid_map (
    old_id text PRIMARY KEY,
    new_id uuid NOT NULL UNIQUE
) ON COMMIT DROP;

INSERT INTO device_guid_map (old_id, new_id)
SELECT device_id, CASE
    WHEN device_id = 'bowl-01' THEN '7e8c0d1a-6b39-47b5-928a-64f3f1039e21'::uuid
    WHEN pg_input_is_valid(device_id, 'uuid') THEN CASE
        WHEN device_id::uuid <> '00000000-0000-0000-0000-000000000000'::uuid THEN device_id::uuid
        ELSE gen_random_uuid()
    END
    ELSE gen_random_uuid()
END FROM devices;

ALTER TABLE weight_readings DROP CONSTRAINT weight_readings_device_id_fkey;
ALTER TABLE devices ADD COLUMN legacy_device_id text UNIQUE;

UPDATE devices AS device
SET legacy_device_id = device.device_id, device_id = mapping.new_id::text
FROM device_guid_map AS mapping WHERE device.device_id = mapping.old_id;

UPDATE weight_readings AS reading SET device_id = mapping.new_id::text
FROM device_guid_map AS mapping WHERE reading.device_id = mapping.old_id;

ALTER TABLE devices ALTER COLUMN device_id TYPE uuid USING device_id::uuid;
ALTER TABLE weight_readings ALTER COLUMN device_id TYPE uuid USING device_id::uuid;

ALTER TABLE devices ADD CONSTRAINT ck_devices_device_id_nonempty
    CHECK (device_id <> '00000000-0000-0000-0000-000000000000'::uuid);
ALTER TABLE weight_readings ADD CONSTRAINT weight_readings_device_id_fkey
    FOREIGN KEY (device_id) REFERENCES devices(device_id);
