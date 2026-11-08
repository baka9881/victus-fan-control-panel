# References

The model-specific protocol work and community reports below informed this tool's HP WMI call sequence, firmware interpretation and test plan:

- [Ohman](https://github.com/P4R1H/ohman), especially its hardware-interface documentation and firmware handoff research.
- [OmenMon](https://github.com/OmenMon/OmenMon), including the [documented BIOS operations](https://omenmon.github.io/cli).
- [OmenCore's 88F8 field reports](https://github.com/theantipopau/omencore/issues/207), which show why a V0 software-control flag of zero alone does not establish lack of manual fan control.
- [Linux HP WMI driver](https://github.com/torvalds/linux/blob/master/drivers/platform/x86/hp/hp-wmi.c).
- [HP's temperature and performance control documentation](https://support.hp.com/au-en/document/ish_4052969-4031879-16).

Upstream projects retain their own licenses. Their tools, drivers and diagnostic data are not bundled in this repository or its portable release. NVIDIA NVML is loaded from the user's existing NVIDIA driver; it is not redistributed here. HP and Victus names identify the intended hardware; this is an independent, unofficial project.
