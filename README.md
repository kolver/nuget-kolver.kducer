# nuget kolver.kducer package
This library allows you to interface with a Kolver K-Ducer torque driving system without having to worry about the underlying Modbus TCP protocol.  
You can obtain tightening results, enable/disable the screwdriver, and run the screwdriver remotely (read the manual and always follow all safety precautions before running the screwdriver from the library).  
It's compatible with .NET standard 2.0 (backwards compatible with .NET framework)  
Brought to you by Kolver www.kolver.com
## Installation
```
dotnet add package Kolver.Kducer
```
## Usage
Every public method is commented, so if you are using Visual Studio you can see the documentation via IntelliSense as you code.
Also see the Examples folder.
### Instantiate a Kducer client and verify if connection was successful
```C#
// create a Kducer client. each KDU-1A should have its own client
// The cyclic async TCP/IP communication loop is started immediately after instantiation automatically in the background
Kducer kdu = new Kducer("192.168.32.103");
// the next two lines don't initiate a connection (Kducer connects automatically), they just verify if the connection was successful
bool success = await kdu.IsConnectedWithTimeoutAsync(500); // waits up to 500ms for TCP/IP connection to be estabilished
bool success = kdu.IsConnectedWithTimeoutBlocking(500); // blocks up to 500ms for TCP/IP connection to be established
// Kducer automatically reconnects if the connection drops. See connection management for more.
// kdu.Dispose(); // call Dispose when done using
```
### Obtaining results
The `Kducer` object maintains an internal async tasks that periodically (every 100ms by default) polls the KDU-1A over Modbus TCP for new results, and/or executes the other operations requested.  
When new results are received, the `Kducer` puts them on an internal FIFO queue (first in is first out).  
The object type in the queue is `KducerTighteningResult`, which provides a lot of methods for getting different data about the tightening result.  
When you retrieve results using `GetResultAsync` or `GetResult`, the tightening result is removed from the `Kducer` internal queue.  
This mechanism ensures that you can process all tightening results independently of how frequently you check if a new result is present.  
When your application needs to wait for a new result and/or is expecting a result, you can await `GetResultAsync` (with an optional cancellation token and an optional bool parameter to throw an exception if the connection drops while awaiting).  
You can also check if at least one result is available via `HasNewResult()` and obtain it synchronously with `GetResult`.  
### Print a tightening result using async
```C#
// print a tightening result using async/await. You can optionally pass a CancellationToken to stop the task
KducerTighteningResult lastesTightening = await kdu.GetResultAsync(CancellationToken.None);
Console.WriteLine($"{lastesTightening.GetResultTimestamp()} - The torque was {lastesTightening.GetTorqueResult()} cNm and the angle was {lastesTightening.GetAngleResult()} degrees");
```
### Detecting a disconnection while awaiting for a async result
```C#
// pass true on the second parameter of GetResultAsync to get a SocketException if the connection to the KDU is dropped for any reason while waiting
KducerTighteningResult resultOrExceptionIfDC = await kdu.GetResultAsync(CancellationToken.None, true);
```
### Print a tightening result without using async
```C#
// print a tightening result without using async/await
while (true)
{
    if (kdu.HasNewResult())
    {
        KducerTighteningResult latestResult = kdu.GetResult();
        Console.WriteLine($"{latestResult.GetResultTimestamp()} - The torque was {latestResult.GetTorqueResult()} cNm and the angle was {latestResult.GetAngleResult()} degrees");
        break;
    }
}
```
### Automatically disable (lock) the screwdriver after receiving a result
```C#
// LockScrewdriverIndefinitelyAfterResult
kdu.LockScrewdriverIndefinitelyAfterResult(true); // with this option, kdu will automatically disable the screwdriver after a new result is detected
// the screwdriver is disabled as soon as the result is detected, independently of whether you are awaiting the result or not
KducerTighteningResult result = await kdu.GetResultAsync(CancellationToken.None);
// a new result was obtained, which means the screwdriver is disabled and the operator cannot use it
// use EnableScrewdriver() to re-enable it:
await kdu.EnableScrewdriver();
```
### Enable high resolution Torque/Angle graphs with each tightening result
```C#
await kdu.SetHighResGraphModeAsync(true); // only works with KDU-1A v38 and later, throws exception with earlier versions
```
### Select a program on the K-Ducer controller
```C#
// select a program on the KDU controller
try
{
    await kdu.SelectProgramNumberAsync(60); // select programn number 60. this method checks if program 60 is already selected and shortcuts the return if so. if the KDU is in sequence mode, this method sets it to program mode
}
catch (ModbusException)
{
    // the KDU controller was not ready to accept the new program
    // for example if the screwdriver was running when you issued the command
    throw;
}
catch (SocketException)
{
    // exception on underlying TCP connection (cable physically disconnected, power loss...)
    throw;
}
// you can also read if the program is still selected
await kdu.GetProgramNumberAsync();
```
### Reading the selected program
```C#
ushort programNumber = await kdu.GetProgramNumberAsync();
```
There are a analogous methods for selecting sequences
### Modifying program parameters
Use this functionality at your own risk.  
When using this functionality, make sure you test it appropriately for safety and correctness.  
There are safety implications to setting program parameters incorrectly (for example, accidentally typing an extra 0 and setting the torque ten times higher than you intended).  
Double check of the measurement units of each parameter, indicated on each function (they will appear on visual studio via intellisense).  
There are no validity checks when setting parameters on the `KducerTighteningProgram` object, but the KDU-1A will bounds-check values being sent to it and will return a Modbus Exception if invalid values are sent. These bounds checks are only for validity, do not rely on them for safety.  
```C#
// create a program with default values for a given KDS screwdriver model
// choose from: KDS-MT1.5 (default), KDS-PL6, KDS-PL10, KDS-PL15, KDS-PL20, KDS-PL30, KDS-PL35, KDS-PL45, KDS-PL50, KDS-PL70, KDS-PL3
KducerTighteningProgram programFromScratch = new KducerTighteningProgram("KDS-PL6");
// or you can load the currently selected program from the connected kdu
KducerTighteningProgram programFromKdu = await kdu.GetActiveTighteningProgramDataAsync();
// modify some parameters
programFromScratch.SetFinalSpeed(355); // RPM. the units are specified in the function documentation, you should see them via intellisense
programFromScratch.SetTorqueTarget(50); // cNm. the units are specified in the function documentation, you should see them via intellisense
// send the new program to the KDU-1A as program number 15. there are also methods for sending or getting a dictionary of multiple programs in a single command
await kdu.SendNewProgramDataAsync(15, programFromScratch);
// to serialize a program for storing in a database, get its byte array representation (note: this does not include the program number, but includes all the program parameters)
byte[] serializedProgram = programFromScratch.getProgramModbusHoldingRegistersAsByteArray();
// recreate the program from the serialized bytes
KducerTighteningProgram recreatedProgram = new KducerTighteningProgram(serializedProgram);
```
There is equivalent functionality for creating and uploading sequences and general settings of the KDU controller.
### Update the datetime on the KDU controller
```C#
await kdu.SetDateTimeToNowAsync(); // only works with KDU-1A v40 and later, throws exception with earlier versions
```
### Connection management
#### Internal Modbus TCP/IP task
The Kducer object maintains an internal async tasks that periodically polls the KDU-1A over Modbus TCP for new results, and/or executes the other operations requested.  
The default polling interval is 100ms, which can be adjusted by calling the corresponding method on the Kducer instance.  
The default socket timeout is 300ms, which can only be adjusted via the constructor when instantaiting a Kducer.  
These two values work well in a LAN connection. If conncting to a remote KDU-1A over the internet, you may need to increase them.  
#### Automatic reconnection
For every polling message, if the KDU-1A does not respons within 300ms, the Kducer instance discards the socket and attempts to reconnect.  
This bypasses the TCP/IP retry mechanism of the operating system, which cannot easily be modified.  
The Kducer will continue to attempt to reconnect indefinitley.  
  
To stop the Kducer internal async TCP/IP task, you need to explicitly call Dispose() on your Kducer instance.  
If you don't call Dispose(), the internal task will continue to operate until the Kducer instance is garbage collected.  
If your application terminates, there's no need to call Dispose() first (the Kducer task belongs to your application, it is not spawned on some other process).  
  
When instantiating a Kducer, you can optionally pass a socket timeout parameter.
## Roadmap
Future features:
- customer requests!
- support for KDU-NT series controller
## License
MIT License

Copyright (c) 2024 Kolver Srl www.kolver.com

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
