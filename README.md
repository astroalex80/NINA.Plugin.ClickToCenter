# N.I.N.A. Plugin Click-to-Center

[![.NET](https://img.shields.io/badge/.NET-8.0-blue)](https://dotnet.microsoft.com/)
[![License: MPL](https://img.shields.io/badge/License-MPL-green.svg)](LICENSE.txt)

A plugin for [N.I.N.A.](https://nighttime-imaging.eu/) that lets you click a point in the currently displayed image and slew/center the telescope on that target position.

## What it does
- Right-click a point in the image of the Click to Center dockable window
- A crosshair marker indicates the selected point
- The plugin plate-solves the image and converts the clicked position to target coordinates and a slew/center is triggered.

## Requirements
- N.I.N.A. minimun version 3.0 installed
- Working plate solving configuration in N.I.N.A.
- Mount and camera connected

## Installation
1. Use N.I.N.A. plugin downloader or download the latest release `.zip` from GitHub Releases.
3. Extract the plugin folder with its dll into your N.I.N.A. plugins folder (typical path): `%LOCALAPPDATA%\NINA\Plugins\`
4. Restart N.I.N.A.

## Usage
1. Open the dockable: `Click-to-Center`
3. Capture an image
4. Right-Click on the desired position in the image
5. Click Button **Slew and center**
