@echo off
cls
if not exist packages\FAKE\tools\Fake.exe (
  .paket\paket.exe install -v
)
packages\FAKE\tools\FAKE.exe build.fsx %*
