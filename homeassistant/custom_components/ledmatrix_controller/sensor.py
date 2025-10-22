"""Sensor platform for LED Matrix Controller."""
from __future__ import annotations

import logging

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

