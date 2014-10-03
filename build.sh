#!/bin/bash
if [ ! -f packages/FAKE/tools/FAKE.exe ]; then
  mono .paket/Paket.exe install -v
fi
mono packages/FAKE/tools/FAKE.exe build.fsx $@
