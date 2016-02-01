

###SN76477 Emulator

The SN76477 is an 80s classic home computer sound chip from Texas Instrument.

This application allows setting component values and pin configuration to model sound effects interactively.

Configurations can be saved to file, simulation output can be saved as wav.

I used it for hardware construction, but it can be used to produce sound files as well. 

It is not developed any further - but if you have questions 
feel free to drop me a line and I'll try to remember how this works.

###Screenshot

![alt tag](https://raw.githubusercontent.com/nocoolnicksleft/SN76477/master/screenshot.png)

###Notes

It will compile under Visual Studio 2015 and .NET 4, but it will not run if built as x64 or "Any CPU". 
Switch to x86 and make sure these settings are in app.config:

...<startup useLegacyV2RuntimeActivationPolicy="true">...

<runtime>
  <NetFx40_LegacySecurityPolicy enabled="true"/>
</runtime>

###Credits

Core emulation code by Zsolt Vasvari from the [MAME project](https://github.com/mamedev/mame) which I have ported from C++ to C#.

###Copyright
Copyright (c) 2007, 2016 Bjoern Seip
nocoolnicksleft@gmail.com