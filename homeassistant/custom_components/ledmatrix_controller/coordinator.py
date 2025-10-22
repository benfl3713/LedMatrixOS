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
                
                return {
                    "settings": settings,
                    "apps": apps_data.get("apps", []),
                    "active_app": apps_data.get("activeApp"),
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

