"""Light platform for LED Matrix Controller."""
from __future__ import annotations

import logging
from typing import Any

from homeassistant.components.light import (
    ATTR_BRIGHTNESS,
    ColorMode,
    LightEntity,
)
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
    """Set up LED Matrix light from a config entry."""
    coordinator: LedMatrixCoordinator = hass.data[DOMAIN][entry.entry_id]
    async_add_entities([LedMatrixLight(coordinator, entry)])


class LedMatrixLight(CoordinatorEntity[LedMatrixCoordinator], LightEntity):
    """Representation of an LED Matrix as a light."""

    _attr_has_entity_name = True
    _attr_name = None
    _attr_color_mode = ColorMode.BRIGHTNESS
    _attr_supported_color_modes = {ColorMode.BRIGHTNESS}

    def __init__(
        self,
        coordinator: LedMatrixCoordinator,
        entry: ConfigEntry,
    ) -> None:
        """Initialize the light."""
        super().__init__(coordinator)
        self._attr_unique_id = f"{entry.entry_id}_light"
        self._attr_device_info = {
            "identifiers": {(DOMAIN, entry.entry_id)},
            "name": "LED Matrix",
            "manufacturer": "LedMatrixOS",
            "model": "LED Matrix Display",
        }

    @property
    def is_on(self) -> bool:
        """Return true if light is on."""
        if self.coordinator.data:
            return self.coordinator.data.get("settings", {}).get("isEnabled", False)
        return False

    @property
    def brightness(self) -> int | None:
        """Return the brightness of this light between 0..255."""
        if self.coordinator.data:
            # API returns 0-100, convert to 0-255
            brightness = self.coordinator.data.get("settings", {}).get("brightness", 100)
            return int(brightness * 255 / 100)
        return None

    async def async_turn_on(self, **kwargs: Any) -> None:
        """Turn the light on."""
        if ATTR_BRIGHTNESS in kwargs:
            # Convert from 0-255 to 0-100
            brightness = int(kwargs[ATTR_BRIGHTNESS] * 100 / 255)
            await self.coordinator.set_brightness(brightness)
        
        await self.coordinator.set_power(True)
        await self.coordinator.async_request_refresh()

    async def async_turn_off(self, **kwargs: Any) -> None:
        """Turn the light off."""
        await self.coordinator.set_power(False)
        await self.coordinator.async_request_refresh()
