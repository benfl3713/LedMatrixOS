"""LED Matrix Controller integration for Home Assistant."""
from __future__ import annotations

import logging
from typing import Any

from homeassistant.config_entries import ConfigEntry
from homeassistant.const import CONF_HOST, CONF_PORT, Platform
import voluptuous as vol

from homeassistant.core import HomeAssistant, ServiceCall
from homeassistant.helpers import config_validation as cv
from homeassistant.helpers.aiohttp_client import async_get_clientsession

from .coordinator import LedMatrixCoordinator

_LOGGER = logging.getLogger(__name__)

PLATFORMS: list[Platform] = [
    Platform.LIGHT,
    Platform.SELECT,
    Platform.SENSOR,
    Platform.NUMBER,
]

DOMAIN = "ledmatrix_controller"

SERVICE_SHOW_TOAST = "show_toast"
SERVICE_SET_BADGE = "set_badge"
SERVICE_DISMISS_OVERLAY = "dismiss_overlay"
SERVICE_RELOAD_SCHEDULE = "reload_schedule"

TOAST_SCHEMA = vol.Schema(
    {
        vol.Required("message"): cv.string,
        vol.Optional("seconds", default=4): vol.All(vol.Coerce(float), vol.Range(min=1, max=60)),
        vol.Optional("color"): cv.string,
        vol.Optional("background"): cv.string,
    }
)
BADGE_SCHEMA = vol.Schema(
    {
        vol.Required("id"): cv.string,
        vol.Optional("color"): cv.string,
        vol.Optional("pulsing", default=True): cv.boolean,
    }
)
DISMISS_SCHEMA = vol.Schema({vol.Optional("id"): cv.string})


def _coordinators(hass: HomeAssistant) -> list[LedMatrixCoordinator]:
    return list(hass.data.get(DOMAIN, {}).values())


def _register_services(hass: HomeAssistant) -> None:
    """Register the domain services once; each one is sent to every configured matrix."""
    if hass.services.has_service(DOMAIN, SERVICE_SHOW_TOAST):
        return

    async def show_toast(call: ServiceCall) -> None:
        for coordinator in _coordinators(hass):
            await coordinator.show_toast(
                call.data["message"], call.data["seconds"], call.data.get("color"), call.data.get("background")
            )

    async def set_badge(call: ServiceCall) -> None:
        for coordinator in _coordinators(hass):
            await coordinator.set_badge(call.data["id"], call.data.get("color"), call.data["pulsing"])

    async def dismiss_overlay(call: ServiceCall) -> None:
        for coordinator in _coordinators(hass):
            await coordinator.dismiss_overlay(call.data.get("id"))

    async def reload_schedule(call: ServiceCall) -> None:
        for coordinator in _coordinators(hass):
            await coordinator.reload_schedule()

    hass.services.async_register(DOMAIN, SERVICE_SHOW_TOAST, show_toast, schema=TOAST_SCHEMA)
    hass.services.async_register(DOMAIN, SERVICE_SET_BADGE, set_badge, schema=BADGE_SCHEMA)
    hass.services.async_register(DOMAIN, SERVICE_DISMISS_OVERLAY, dismiss_overlay, schema=DISMISS_SCHEMA)
    hass.services.async_register(DOMAIN, SERVICE_RELOAD_SCHEDULE, reload_schedule, schema=vol.Schema({}))


async def async_setup_entry(hass: HomeAssistant, entry: ConfigEntry) -> bool:
    """Set up LED Matrix Controller from a config entry."""
    host = entry.data[CONF_HOST]
    port = entry.data.get(CONF_PORT, 5005)
    
    session = async_get_clientsession(hass)
    coordinator = LedMatrixCoordinator(hass, session, host, port)
    
    await coordinator.async_config_entry_first_refresh()
    
    hass.data.setdefault(DOMAIN, {})
    hass.data[DOMAIN][entry.entry_id] = coordinator
    
    await hass.config_entries.async_forward_entry_setups(entry, PLATFORMS)
    _register_services(hass)

    return True


async def async_unload_entry(hass: HomeAssistant, entry: ConfigEntry) -> bool:
    """Unload a config entry."""
    if unload_ok := await hass.config_entries.async_unload_platforms(entry, PLATFORMS):
        hass.data[DOMAIN].pop(entry.entry_id)
        if not hass.data[DOMAIN]:
            for service in (SERVICE_SHOW_TOAST, SERVICE_SET_BADGE, SERVICE_DISMISS_OVERLAY, SERVICE_RELOAD_SCHEDULE):
                hass.services.async_remove(DOMAIN, service)
    
    return unload_ok

