"""Sensor platform for LED Matrix Controller."""
from __future__ import annotations

import logging
from datetime import datetime

from homeassistant.components.sensor import SensorEntity, SensorStateClass
from homeassistant.config_entries import ConfigEntry
from homeassistant.const import UnitOfInformation
from homeassistant.core import HomeAssistant
from homeassistant.helpers.entity_platform import AddEntitiesCallback
from homeassistant.helpers.update_coordinator import CoordinatorEntity

from .const import DOMAIN
from .coordinator import LedMatrixCoordinator

_LOGGER = logging.getLogger(__name__)


async def async_setup_entry(
    hass: HomeAssistant,
    entry: ConfigEntry,
    async_add_entities: AddEntitiesCallback,
) -> None:
    """Set up LED Matrix sensors from a config entry."""
    coordinator: LedMatrixCoordinator = hass.data[DOMAIN][entry.entry_id]
    async_add_entities([
        LedMatrixFpsSensor(coordinator, entry),
        LedMatrixStatusSensor(coordinator, entry),
        LedMatrixSchedulePlaylistSensor(coordinator, entry),
        LedMatrixScheduleNextChangeSensor(coordinator, entry),
        LedMatrixActiveOverlaysSensor(coordinator, entry),
    ])


class LedMatrixFpsSensor(CoordinatorEntity[LedMatrixCoordinator], SensorEntity):
    """Sensor for LED Matrix FPS."""

    _attr_has_entity_name = True
    _attr_name = "FPS"
    _attr_icon = "mdi:speedometer"
    _attr_native_unit_of_measurement = "fps"
    _attr_state_class = SensorStateClass.MEASUREMENT

    def __init__(
        self,
        coordinator: LedMatrixCoordinator,
        entry: ConfigEntry,
    ) -> None:
        """Initialize the sensor."""
        super().__init__(coordinator)
        self._attr_unique_id = f"{entry.entry_id}_fps"
        self._attr_device_info = {
            "identifiers": {(DOMAIN, entry.entry_id)},
            "name": "LED Matrix",
            "manufacturer": "LedMatrixOS",
            "model": "LED Matrix Display",
        }

    @property
    def native_value(self) -> int | None:
        """Return the FPS."""
        if self.coordinator.data:
            return self.coordinator.data.get("settings", {}).get("fps")
        return None


class LedMatrixStatusSensor(CoordinatorEntity[LedMatrixCoordinator], SensorEntity):
    """Sensor for LED Matrix status."""

    _attr_has_entity_name = True
    _attr_name = "Status"
    _attr_icon = "mdi:information-outline"

    def __init__(
        self,
        coordinator: LedMatrixCoordinator,
        entry: ConfigEntry,
    ) -> None:
        """Initialize the sensor."""
        super().__init__(coordinator)
        self._attr_unique_id = f"{entry.entry_id}_status"
        self._attr_device_info = {
            "identifiers": {(DOMAIN, entry.entry_id)},
            "name": "LED Matrix",
            "manufacturer": "LedMatrixOS",
            "model": "LED Matrix Display",
        }

    @property
    def native_value(self) -> str | None:
        """Return the status."""
        if self.coordinator.data:
            settings = self.coordinator.data.get("settings", {})
            is_running = settings.get("isRunning", False)
            is_enabled = settings.get("isEnabled", False)
            
            if is_running and is_enabled:
                return "Running"
            elif is_running:
                return "Running (Disabled)"
            else:
                return "Stopped"
        return None

    @property
    def extra_state_attributes(self) -> dict[str, any]:
        """Return additional attributes."""
        if self.coordinator.data:
            settings = self.coordinator.data.get("settings", {})
            return {
                "width": settings.get("width"),
                "height": settings.get("height"),
                "is_running": settings.get("isRunning"),
            }
        return {}


class LedMatrixSchedulePlaylistSensor(CoordinatorEntity[LedMatrixCoordinator], SensorEntity):
    """Sensor for LED Matrix active schedule playlist."""

    _attr_has_entity_name = True
    _attr_name = "Schedule playlist"
    _attr_icon = "mdi:playlist-music"

    def __init__(
        self,
        coordinator: LedMatrixCoordinator,
        entry: ConfigEntry,
    ) -> None:
        """Initialize the sensor."""
        super().__init__(coordinator)
        self._attr_unique_id = f"{entry.entry_id}_schedule_playlist"
        self._attr_device_info = {
            "identifiers": {(DOMAIN, entry.entry_id)},
            "name": "LED Matrix",
            "manufacturer": "LedMatrixOS",
            "model": "LED Matrix Display",
        }

    @property
    def native_value(self) -> str | None:
        """Return the playlist name."""
        if self.coordinator.data:
            schedule_status = self.coordinator.data.get("schedule_status")
            if schedule_status:
                return schedule_status.get("playlist") or "none"
        return None

    @property
    def extra_state_attributes(self) -> dict:
        """Return additional attributes."""
        if self.coordinator.data:
            schedule_status = self.coordinator.data.get("schedule_status")
            if schedule_status and schedule_status.get("activeRule"):
                active_rule = schedule_status["activeRule"]
                return {
                    "rule_priority": active_rule.get("priority"),
                    "rule_condition": active_rule.get("condition"),
                    "entry_index": schedule_status.get("entryIndex"),
                    "entry_count": schedule_status.get("entryCount"),
                }
        return {}


class LedMatrixScheduleNextChangeSensor(CoordinatorEntity[LedMatrixCoordinator], SensorEntity):
    """Sensor for LED Matrix schedule next change timestamp."""

    _attr_has_entity_name = True
    _attr_name = "Schedule next change"
    _attr_icon = "mdi:clock-outline"
    _attr_device_class = "timestamp"

    def __init__(
        self,
        coordinator: LedMatrixCoordinator,
        entry: ConfigEntry,
    ) -> None:
        """Initialize the sensor."""
        super().__init__(coordinator)
        self._attr_unique_id = f"{entry.entry_id}_schedule_next_change"
        self._attr_device_info = {
            "identifiers": {(DOMAIN, entry.entry_id)},
            "name": "LED Matrix",
            "manufacturer": "LedMatrixOS",
            "model": "LED Matrix Display",
        }

    @property
    def native_value(self) -> datetime | None:
        """Return the next change timestamp."""
        if self.coordinator.data:
            schedule_status = self.coordinator.data.get("schedule_status")
            if schedule_status and schedule_status.get("nextChange"):
                next_change = schedule_status["nextChange"]
                try:
                    return datetime.fromisoformat(next_change.replace("Z", "+00:00"))
                except (ValueError, AttributeError):
                    return None
        return None

    @property
    def extra_state_attributes(self) -> dict:
        """Return additional attributes."""
        if self.coordinator.data:
            schedule_status = self.coordinator.data.get("schedule_status")
            if schedule_status:
                return {
                    "reason": schedule_status.get("nextChangeReason"),
                }
        return {}


class LedMatrixActiveOverlaysSensor(CoordinatorEntity[LedMatrixCoordinator], SensorEntity):
    """Sensor for LED Matrix active overlays count."""

    _attr_has_entity_name = True
    _attr_name = "Active overlays"
    _attr_icon = "mdi:layers-multiple"
    _attr_native_unit_of_measurement = "overlays"
    _attr_state_class = SensorStateClass.MEASUREMENT

    def __init__(
        self,
        coordinator: LedMatrixCoordinator,
        entry: ConfigEntry,
    ) -> None:
        """Initialize the sensor."""
        super().__init__(coordinator)
        self._attr_unique_id = f"{entry.entry_id}_active_overlays"
        self._attr_device_info = {
            "identifiers": {(DOMAIN, entry.entry_id)},
            "name": "LED Matrix",
            "manufacturer": "LedMatrixOS",
            "model": "LED Matrix Display",
        }

    @property
    def native_value(self) -> int | None:
        """Return the overlay count."""
        if self.coordinator.data:
            overlays = self.coordinator.data.get("overlays")
            if overlays is not None:
                return len(overlays)
        return None

    @property
    def extra_state_attributes(self) -> dict:
        """Return additional attributes."""
        if self.coordinator.data:
            overlays = self.coordinator.data.get("overlays")
            if overlays:
                return {
                    "overlays": [
                        {
                            "id": overlay.get("id"),
                            "kind": overlay.get("kind"),
                            "text": overlay.get("text"),
                            "priority": overlay.get("priority"),
                            "remaining_seconds": overlay.get("remainingSeconds"),
                        }
                        for overlay in overlays
                    ]
                }
        return {}

