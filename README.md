# ZV Cluster Monitor App

This single-project self contained app includes:
- DX cluster telnet client (basic IAC handling)
- single self-contained app, just click and run, no installer.
- Multi-cluster management, auto-connect/reconnect
- Per-cluster login and default commands
- Color-coded spot list with per-band colors
- CI-V / Yaesu / Kenwood CAT support (configurable)
- Spot list context manu
	- right click on a spot for context menu
	- Jump radio to selected spot 
	- QRZ / DXNews lookup via default browser
	- right click for context menu, Jump Radio command
- RBN lookup (if applicable based on mode)
- Settings UI and cluster editor
- ToolTips (hover over)
- Cluster server
- Cluster console
	- see what the dx cluster is saying in real time
	- ad-hoc cluster commands can be sent to cluter server as desired
- Color picker and font picker
- DXP Calendar, announced DXP's, SolarHam, ContestCalendar lookup via browser (in View menu)
- Window splitter
	- slowly move the mouse up from the bottom of the spot display, wait until mouse icon changes
	- right-click and drag window splitter up or down to change the proportion of the dialog box dedicated to 
		the console and dx spots. 

Band Filtering 
	- click on a band to make band visible/invisible in the list
	- right click to see ONLY that band (solo mode)
	- click to restore

Mode Filtering 
	- as above, same behavior
	- inferences made to detect mode (spot protocol has no distinc field for mode).
		if freq is in FTx windows, mode DAT
		if freq is in SSB windows, mode is SSB
		if freq is not in either of above, mode is inferred to be CW
	- imperfect, but close

DX Filtering - 
	- for chasing specific calls/DXP's etc.  
	- right click to editor to create the list, handles both * and ? wildcards

Cluster server - connect your other cluster enabled appls to the monitor server to share one cluster connection in your hamshack, all inheriting the same config.

HOW TO get started
	- navigate to View->Settings	
	- Input
		Callsign
		GMT Offset (-6 for IL)
		Grid Square (needed for great circle bearing/distance)
		Enable debug logging or not
		enable/configure CAT settings.
	- enable AutoLogin for AI9T cluster.
	- Save and CONNECT on main dialog, or exit/restart.

- Consider default cluster commands (in Edit Cluster dialog) desired.   
	these commands run whenever a new connection is made.
	these commands are associated with your login (callsign) and retained across login sessions.
	comments in/out via # character
	default now is to pass only spots ORIGINATING in K/VE, and reject spots for K/VE (show DX only).
	edit as you see fit. 
	
Recommend leaving defaults as they are until you are comfortable with it. 

Build:
	Requires .NET 9.0 SDK or later, but all libraries are linked into a single executable.  Just click and run

Notes:
	- Settings are saved to `ZVClusterApp.settings.json`.
	- Debug logging if enabled to "debug.log" file. 
	- CAT commands and exact CI-V sequences are basic and may need tuning per radio model.  Tested on Icom
	- debug.log file if enable has no limitation on size, nor is managed.  if it gets too large, or if a new
		version is installed used, recommend deleting the debug.log file.
	- when reporting bugs, attach the logfile and the estimated time of the fault if possible. 

Known limitations:
	- only tested on CC-Cluster servers (for now), using default of AI9T (Marshall, ILL)
	- CAT only tested on Icom Radios, need volunteers to test on Yaesu/Kenwood.
	- code for Yaesu and Kenwood radios has been coded in the blind (I dont own a Yaesu CATable radio).
	- NO efforts are made to migrate existing .json settings if format/content of .json file configuration
		changes.   when in doubt, delete the json file BEFORE starting appl. 



