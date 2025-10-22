# LED Matrix Controller for Home Assistant

A custom Home Assistant integration to control your LedMatrixOS device.

## Features

This integration provides the following entities for your LED Matrix device:

- **Light Entity**: Control power and brightness of the LED matrix
- **Select Entity**: Choose which app to display on the matrix
- **Number Entity**: Adjust brightness with a slider (0-100)
- **Sensor Entities**: 
  - FPS sensor showing current frame rate
  - Status sensor showing device state with dimensions as attributes

## Installation

### Method 1: Manual Installation

1. Copy the `custom_components/ledmatrix_controller` folder to your Home Assistant `config/custom_components/` directory
2. Restart Home Assistant
3. Go to **Settings** → **Devices & Services** → **Add Integration**
4. Search for "LED Matrix Controller"
5. Enter your LED Matrix device's IP address and port (default: 5005)

### Method 2: HACS (Recommended)

1. Add this repository as a custom repository in HACS
2. Search for "LED Matrix Controller" in HACS
3. Install the integration
4. Restart Home Assistant
5. Add the integration through the UI

## Configuration

The integration uses the config flow for setup. You'll need:

- **Host**: The IP address or hostname of your LED Matrix device
- **Port**: The API port (default: 5005)

## Available Entities

After setup, you'll have access to the following entities:

### Light: `light.led_matrix`
Control the matrix power and brightness:
```yaml
service: light.turn_on
target:
  entity_id: light.led_matrix
data:
  brightness: 200
```

### Select: `select.led_matrix_active_app`
Switch between different apps:
```yaml
service: select.select_option
target:
  entity_id: select.led_matrix_active_app
data:
  option: "Animated Clock"
```

Available apps include:
- Clock
- Animated Clock
- Rainbow Spiral
- Geometric Patterns
- Bouncing Balls
- DVD Logo
- Matrix Rain
- Solid Color
- Weather
- Spotify
- And more...

### Number: `number.led_matrix_brightness`
Fine-tune brightness (0-100):
```yaml
service: number.set_value
target:
  entity_id: number.led_matrix_brightness
data:
  value: 75
```

### Sensors
- `sensor.led_matrix_fps`: Current frame rate
- `sensor.led_matrix_status`: Device status (Running, Stopped, etc.)

## Example Automations

### Turn on at sunrise with low brightness
```yaml
automation:
  - alias: "LED Matrix Morning"
    trigger:
      - platform: sun
        event: sunrise
    action:
      - service: light.turn_on
        target:
          entity_id: light.led_matrix
        data:
          brightness: 50
      - service: select.select_option
        target:
          entity_id: select.led_matrix_active_app
        data:
          option: "Clock"
```

### Turn off at night
```yaml
automation:
  - alias: "LED Matrix Night"
    trigger:
      - platform: time
        at: "23:00:00"
    action:
      - service: light.turn_off
        target:
          entity_id: light.led_matrix
```

### Change app based on music playing
```yaml
automation:
  - alias: "LED Matrix Music Visualizer"
    trigger:
      - platform: state
        entity_id: media_player.spotify
        to: "playing"
    action:
      - service: select.select_option
        target:
          entity_id: select.led_matrix_active_app
        data:
          option: "Equalizer"
```

### Brightness based on time of day
```yaml
automation:
  - alias: "LED Matrix Adaptive Brightness"
    trigger:
      - platform: time_pattern
        hours: "/1"
    action:
      - service: number.set_value
        target:
          entity_id: number.led_matrix_brightness
        data:
          value: >
            {% set hour = now().hour %}
            {% if hour >= 6 and hour < 9 %}
              30
            {% elif hour >= 9 and hour < 18 %}
              70
            {% elif hour >= 18 and hour < 22 %}
              50
            {% else %}
              20
            {% endif %}
```

## Lovelace Card Example

```yaml
type: entities
title: LED Matrix
entities:
  - entity: light.led_matrix
  - entity: select.led_matrix_active_app
  - entity: number.led_matrix_brightness
  - entity: sensor.led_matrix_fps
  - entity: sensor.led_matrix_status
```

Or use a picture-elements card for a custom dashboard:

```yaml
type: vertical-stack
cards:
  - type: light
    entity: light.led_matrix
    name: LED Matrix Display
  - type: entities
    entities:
      - entity: select.led_matrix_active_app
        name: Active App
      - entity: number.led_matrix_brightness
        name: Brightness
      - entity: sensor.led_matrix_fps
        icon: mdi:speedometer
      - entity: sensor.led_matrix_status
        icon: mdi:information
```

## Troubleshooting

### Cannot connect to device
- Ensure your LED Matrix device is running and accessible on the network
- Check that the API port (default 5005) is open and not blocked by a firewall
- Verify the host and port in the integration configuration

### Entities not updating
- Check the Home Assistant logs for errors
- The integration polls the device every 5 seconds
- Ensure your device's API is responding correctly

### App list not showing
- Make sure your LED Matrix device has apps registered
- Check that the `/api/apps` endpoint is accessible

## API Documentation

The integration uses the following API endpoints:

- `GET /api/settings` - Get device settings
- `POST /api/settings/brightness/{value}` - Set brightness (0-100)
- `POST /api/settings/power/{enabled}` - Set power state
- `GET /api/apps` - Get list of available apps
- `POST /api/apps/{id}` - Activate an app

## Support

For issues and feature requests, please visit the [GitHub repository](https://github.com/ledmatrix/LedMatrixOS).

## License

This integration is released under the MIT License.

