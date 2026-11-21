CREATE TABLE bowl_alert_state (
    device_id uuid PRIMARY KEY REFERENCES devices(device_id),
    observed_at timestamptz NOT NULL,
    sequence bigint NOT NULL,
    empty boolean NOT NULL
);
INSERT INTO bowl_alert_state
SELECT DISTINCT ON (device_id) device_id, observed_at, sequence, false
FROM weight_readings WHERE observed_at <= now() + interval '1 minute'
ORDER BY device_id, observed_at DESC, sequence DESC;

CREATE TABLE bowl_notifications (
    id uuid PRIMARY KEY,
    device_id uuid NOT NULL REFERENCES devices(device_id),
    created_at timestamptz NOT NULL,
    resolved_at timestamptz,
    weight_grams numeric NOT NULL
);
CREATE UNIQUE INDEX ix_one_empty_alert ON bowl_notifications(device_id) WHERE resolved_at IS NULL;
CREATE INDEX ix_bowl_notifications_created ON bowl_notifications(created_at DESC);
CREATE TABLE web_push_identity (
    singleton boolean PRIMARY KEY DEFAULT true CHECK (singleton),
    public_key text NOT NULL,
    private_key text NOT NULL
);
CREATE TABLE web_push_subscriptions (
    id uuid PRIMARY KEY,
    endpoint text UNIQUE NOT NULL,
    p256dh text NOT NULL,
    auth text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE web_push_deliveries (
    notification_id uuid NOT NULL REFERENCES bowl_notifications(id),
    subscription_id uuid NOT NULL REFERENCES web_push_subscriptions(id) ON DELETE CASCADE,
    attempts integer NOT NULL DEFAULT 0,
    next_attempt_at timestamptz NOT NULL DEFAULT now(),
    finished_at timestamptz,
    PRIMARY KEY (notification_id, subscription_id)
);
