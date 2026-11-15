import network
import time
from settings import WIFI_SSID, WIFI_PASSWORD

# Initialise the Wi-Fi interface
wifi = network.WLAN(network.WLAN.IF_STA)
wifi.active(True)

print("Smart Dog Bowl starting...")

if not wifi.isconnected():
    print("Connecting to Wi-Fi...")
    wifi.connect(WIFI_SSID, WIFI_PASSWORD)

    start = time.ticks_ms()

    while not wifi.isconnected():
        if time.ticks_diff(time.ticks_ms(), start) > 20000:
            raise RuntimeError(
                "Wi-Fi connection timed out. Status: {}".format(
                    wifi.status()
                )
            )
        time.sleep(0.5)

print("Connected successfully!")
print("IP address:", wifi.ifconfig()[0])
print("Wi-Fi status:", wifi.status())

