

Virtual SN76477 
===========================================================================================================

The SN76477 is an 80s classic home computer sound chip from Texas Instrument.

This application allows setting circuit values and pin configuration to model sound effects interactively.

I used it to build hardware for sound, but it can be used to produce sound files as well. 

Or just for a good time your partner will have a hard time understanding.

It is not developed any further - but if you have questions 
feel free to drop me a line and I'll try to remember how this works.

nocoolnicksleft@gmail.com

===========================================================================================================

Credits: 

Core emulation code by Zsolt Vasvari from the MAME project which I have ported from C++ to C#.

===========================================================================================================

It will compile under Visual Studio 2015 and .NET 4, but it will not run if built as x64 or "Any CPU". 
Switch to x86 and make sure these settings are in app.config:

<startup useLegacyV2RuntimeActivationPolicy="true">
 <supportedRuntime version="v2.0.50727"/></startup>
<runtime>
  <NetFx40_LegacySecurityPolicy enabled="true"/>
</runtime>


