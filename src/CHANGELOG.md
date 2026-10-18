# Changelog

All notable changes to NDMicroCpu are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the version numbers follow
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Repository created from the ND-110 emulator repository: the micro code level CPU emulator
  (`NDMicroCpu.Core`), the console machine, the micro code command line, the web emulator, the
  gRPC debugger, the CPU test suite, the INSTRUCTION-VERIFY reference traces and the two
  microprogrammer's manuals.

### Removed

- The web emulator's "Assembly file" panel and the prebuilt ND-100 macro assembler it used.
- The NATS log destination.
