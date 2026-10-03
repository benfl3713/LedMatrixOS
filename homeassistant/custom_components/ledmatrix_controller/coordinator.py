"""Data coordinator for LED Matrix Controller."""
from __future__ import annotations

import asyncio
import logging
from datetime import timedelta
from typing import Any

import aiohttp

from homeassistant.core import HomeAssistant
from homeassistant.helpers.update_coordinator import DataUpdateCoordinator, UpdateFailed

from .const import UPDATE_INTERVAL

_LOGGER = logging.getLogger(__name__)


class LedMatrixCoordinator(DataUpdateCoordinator):
    """Coordinator to manage fetching LED Matrix data."""

    def __init__(
        self,
        hass: HomeAssistant,
        session: aiohttp.ClientSession,
        host: str,
        port: int,
    ) -> None:
        """Initialize the coordinator."""
        self.host = host
        self.port = port
        self.session = session
        self.base_url = f"http://{host}:{port}/api"
        
        super().__init__(
            hass,
            _LOGGER,
            name="LED Matrix Controller",
            update_interval=timedelta(seconds=UPDATE_INTERVAL),
        )

    async def _async_update_data(self) -> dict[str, Any]:
        """Fetch data from API."""
        try:
            async with asyncio.timeout(10):
                # Get device settings
                settings_url = f"{self.base_url}/settings"
                async with self.session.get(settings_url) as response:
                    if response.status != 200:
                        raise UpdateFailed(f"Error fetching settings: {response.status}")
                    settings = await response.json()

                # Get apps list
                apps_url = f"{self.base_url}/apps"
                async with self.session.get(apps_url) as response:
                    if response.status != 200:
                        raise UpdateFailed(f"Error fetching apps: {response.status}")
                    apps_data = await response.json()

                # Get schedule status (tolerate 404 for older servers)
                schedule_status = None
                schedule_url = f"{self.base_url}/schedule/status"
                async with self.session.get(schedule_url) as response:
                    if response.status == 200:
                        schedule_status = await response.json()
                    elif response.status != 404:
                        _LOGGER.warning(f"Error fetching schedule status: {response.status}")

                # Get overlays (tolerate 404 for older servers)
                overlays = None
                overlays_url = f"{self.base_url}/overlays"
                async with self.session.get(overlays_url) as response:
                    if response.status == 200:
                        overlays_data = await response.json()
                        overlays = overlays_data.get("overlays", [])
                    elif response.status != 404:
                        _LOGGER.warning(f"Error fetching overlays: {response.status}")

                return {
                    "settings": settings,
                    "apps": apps_data.get("apps", []),
                    "active_app": apps_data.get("activeApp"),
                    "schedule_status": schedule_status,
                    "overlays": overlays,
                }
        except asyncio.TimeoutError as err:
            raise UpdateFailed(f"Timeout communicating with API") from err
        except aiohttp.ClientError as err:
            raise UpdateFailed(f"Error communicating with API: {err}") from err

    async def set_brightness(self, brightness: int) -> bool:
        """Set the brightness."""
        url = f"{self.base_url}/settings/brightness/{brightness}"
        try:
            async with self.session.post(url) as response:
                return response.status == 200
        except aiohttp.ClientError as err:
            _LOGGER.error("Error setting brightness: %s", err)
            return False

    async def set_power(self, enabled: bool) -> bool:
        """Set the power state."""
        url = f"{self.base_url}/settings/power/{str(enabled).lower()}"
        try:
            async with self.session.post(url) as response:
                return response.status == 200
        except aiohttp.ClientError as err:
            _LOGGER.error("Error setting power: %s", err)
            return False

    async def activate_app(self, app_id: str) -> bool:
        """Activate an app."""
        url = f"{self.base_url}/apps/{app_id}"
        try:
            async with self.session.post(url) as response:
                return response.status == 200
        except aiohttp.ClientError as err:
            _LOGGER.error("Error activating app: %s", err)
            return False

    async def _post_json(self, path: str, payload: dict[str, Any]) -> bool:
        """POST a JSON body and report whether the device accepted it."""
        try:
            async with self.session.post(f"{self.base_url}/{path}", json=payload) as response:
                return response.status == 200
        except aiohttp.ClientError as err:
            _LOGGER.error("Error calling %s: %s", path, err)
            return False

    async def show_toast(
        self,
        message: str,
        seconds: float = 4,
        color: str | None = None,
        background: str | None = None,
    ) -> bool:
        """Show a banner over whatever is running."""
        payload: dict[str, Any] = {"message": message, "seconds": seconds}
        if color:
            payload["color"] = color
        if background:
            payload["background"] = background
        return await self._post_json("overlays/toast", payload)

    async def set_badge(self, badge_id: str, color: str | None = None, pulsing: bool = True) -> bool:
        """Show (or replace) a corner badge until it is dismissed."""
        payload: dict[str, Any] = {"id": badge_id, "pulsing": pulsing}
        if color:
            payload["color"] = color
        return await self._post_json("overlays/badge", payload)

    async def dismiss_overlay(self, overlay_id: str | None = None) -> bool:
        """Dismiss one overlay by id, or all of them when no id is given."""
        path = f"overlays/{overlay_id}" if overlay_id else "overlays"
        try:
            async with self.session.delete(f"{self.base_url}/{path}") as response:
                return response.status == 200
        except aiohttp.ClientError as err:
            _LOGGER.error("Error dismissing overlay: %s", err)
            return False

    async def reload_schedule(self) -> bool:
        """Re-read schedule.json on the device."""
        try:
            async with self.session.post(f"{self.base_url}/schedule/reload") as response:
                return response.status == 200
        except aiohttp.ClientError as err:
            _LOGGER.error("Error reloading schedule: %s", err)
            return False

    async def show_alert(self, message: str, color: str | None = None) -> bool:
        """Show an alert overlay."""
        payload: dict[str, Any] = {"message": message}
        if color:
            payload["color"] = color
        return await self._post_json("notifications/message", payload)

    async def flash_screen(self) -> bool:
        """Flash the screen."""
        try:
            async with self.session.post(f"{self.base_url}/notifications") as response:
                return response.status == 200
        except aiohttp.ClientError as err:
            _LOGGER.error("Error flashing screen: %s", err)
            return False

    async def clear_overlays(self) -> bool:
        """Clear all overlays."""
        try:
            async with self.session.delete(f"{self.base_url}/overlays") as response:
                return response.status == 200
        except aiohttp.ClientError as err:
            _LOGGER.error("Error clearing overlays: %s", err)
            return False
