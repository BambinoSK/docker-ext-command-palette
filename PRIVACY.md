# Privacy Policy

**Docker For Command Palette** (the "extension") is an unofficial, open-source extension for PowerToys Command Palette.

## Summary

- The extension talks only to the Docker engine you use, normally the one on your own PC.
- The developer runs no server and receives no data from you: no analytics, no telemetry, no crash reports, no ads.
- The extension has no account of its own and handles no passwords or registry credentials.
- The only file it writes is a small settings file on your PC.

## What the extension accesses

- **The Docker engine API**, to list and manage your containers, Compose projects, images, volumes and networks, and to read disk usage and engine information. It changes something only when you run a command that does so.
- **The `docker` command-line tool**, which the extension runs for Compose commands, context switching, and the shells, logs and interactive runs it opens in your terminal.
- **Your Docker configuration** (`config.json` and context metadata in your Docker config folder, and the `DOCKER_HOST`, `DOCKER_CONTEXT`, `DOCKER_CONFIG`, `DOCKER_TLS_VERIFY` and `DOCKER_CERT_PATH` environment variables), to find the engine the same way the `docker` command does. It reads endpoint and TLS settings only, not stored registry credentials.
- **Compose files and folders** on your PC, to run Compose commands and to open project folders when you ask.

Container details shown by the extension, such as environment variables, labels and logs, come from your engine and are displayed only on your screen.

## Where data goes

- **Docker engine**: by default over the local named pipe on your PC. If you set **Docker host** to a TCP address, or your docker context points to a remote host, the extension connects to that host. Those connections use TLS when `DOCKER_TLS_VERIFY` is set and are unencrypted otherwise, the same as the `docker` command.
- **Container registries**: when you pull images or run Compose up, your Docker engine downloads images from their registries (for example Docker Hub). The engine does this with its own configuration and credentials. The extension does not contact registries itself.
- **Your browser**: Open published port opens a `localhost` address in your default browser.

Nothing is sent to the developer or to any other third party.

## What is stored on your PC

- **Preferences** (for example Docker host, terminal and editor commands): in a small local `settings.json` in the extension's own settings folder.
- **Docker data** (container, image, volume and network lists, and engine information): kept in memory while the extension runs. The extension does not write it to disk.

## Your choices

- **Change which engine is used:** set **Docker host** in the extension settings, or switch the docker context.
- **Remove everything:** uninstall the extension. This also removes its settings file.

## Contact

Open an issue on the Issues page of this repository.

## Disclaimer

This extension is not affiliated with, endorsed by, or sponsored by Docker, Inc. Docker is a trademark of Docker, Inc.
