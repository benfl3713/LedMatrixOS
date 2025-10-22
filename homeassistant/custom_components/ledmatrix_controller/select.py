"""Select platform for LED Matrix Controller."""
from __future__ import annotations

import logging

from homeassistant.components.select import SelectEntity
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
    """Set up LED Matrix select from a config entry."""
    coordinator: LedMatrixCoordinator = hass.data[DOMAIN][entry.entry_id]
    async_add_entities([LedMatrixAppSelect(coordinator, entry)])


class LedMatrixAppSelect(CoordinatorEntity[LedMatrixCoordinator], SelectEntity):
    """Representation of an LED Matrix app selector."""

    _attr_has_entity_name = True
    _attr_name = "Active App"
    _attr_icon = "mdi:application"

    def __init__(
        self,
        coordinator: LedMatrixCoordinator,
        entry: ConfigEntry,
    ) -> None:
        """Initialize the select entity."""
        super().__init__(coordinator)
        self._attr_unique_id = f"{entry.entry_id}_app_select"
        self._attr_device_info = {
            "identifiers": {(DOMAIN, entry.entry_id)},
            "name": "LED Matrix",
            "manufacturer": "LedMatrixOS",
            "model": "LED Matrix Display",
        }

    @property
    def options(self) -> list[str]:
        """Return a list of available apps."""
        if self.coordinator.data:
            apps = self.coordinator.data.get("apps", [])
            return [app.get("name", app.get("id", "Unknown")) for app in apps]
        return []

    @property
    def current_option(self) -> str | None:
        """Return the current selected app."""
        if self.coordinator.data:
            active_app_id = self.coordinator.data.get("active_app")
            if active_app_id:
                apps = self.coordinator.data.get("apps", [])
                for app in apps:
                    if app.get("id") == active_app_id:
                        return app.get("name", app.get("id"))
        return None

    async def async_select_option(self, option: str) -> None:
        """Change the selected app."""
        if self.coordinator.data:
            apps = self.coordinator.data.get("apps", [])
            for app in apps:
                if app.get("name", app.get("id")) == option:
                    app_id = app.get("id")
                    if app_id:
                        await self.coordinator.activate_app(app_id)
                        await self.coordinator.async_request_refresh()
                        return
        _LOGGER.error("Could not find app: %s", option)

