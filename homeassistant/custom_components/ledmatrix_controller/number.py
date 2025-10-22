"""Number platform for LED Matrix Controller."""
from __future__ import annotations

import logging

from homeassistant.components.number import NumberEntity, NumberMode
from homeassistant.config_entries import ConfigEntry
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
    """Set up LED Matrix number from a config entry."""
    coordinator: LedMatrixCoordinator = hass.data[DOMAIN][entry.entry_id]
    async_add_entities([LedMatrixBrightnessNumber(coordinator, entry)])


class LedMatrixBrightnessNumber(CoordinatorEntity[LedMatrixCoordinator], NumberEntity):
    """Number entity for LED Matrix brightness."""

    _attr_has_entity_name = True
    _attr_name = "Brightness"
    _attr_icon = "mdi:brightness-6"
    _attr_native_min_value = 0
    _attr_native_max_value = 100
    _attr_native_step = 1
    _attr_mode = NumberMode.SLIDER

    def __init__(
        self,
        coordinator: LedMatrixCoordinator,
        entry: ConfigEntry,
    ) -> None:
        """Initialize the number entity."""
        super().__init__(coordinator)
        self._attr_unique_id = f"{entry.entry_id}_brightness_number"
        self._attr_device_info = {
            "identifiers": {(DOMAIN, entry.entry_id)},
            "name": "LED Matrix",
            "manufacturer": "LedMatrixOS",
            "model": "LED Matrix Display",
        }

    @property
    def native_value(self) -> float | None:
        """Return the current brightness."""
        if self.coordinator.data:
            return self.coordinator.data.get("settings", {}).get("brightness", 100)
        return None

    async def async_set_native_value(self, value: float) -> None:
        """Set the brightness."""
        await self.coordinator.set_brightness(int(value))
        await self.coordinator.async_request_refresh()

