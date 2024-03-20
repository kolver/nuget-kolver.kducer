# nuget kolver.kducer package
This library allows you to interface with a Kolver K-Ducer torque driving system without having to worry about the underlying Modbus TCP protocol.
You can obtain tightening results, enable/disable the screwdriver, and run the screwdriver remotely (read the manual and always follow all safety precautions before running the screwdriver from the library).
It's compatible with .NET standard 2.0 (backwards compatible with .NET framework)
Brought to you by Kolver www.kolver.com
## Usage
### Instantiate a client
```C#
// create a Kducer client. The cyclic async communication loop is started automatically in the background. You can optionally pass a ILoggerFactory for logging.
Kducer kdu = new Kducer("192.168.32.103", NullLoggerFactory.Instance);
// kdu.Dispose(); // call Dispose when done using, to stop the Modbus TCP communication loop and close the connection
```
### Print a tightening result using async
```C#
// print a tightening result using async/await. You can optionally pass a CancellationToken to stop the task.
KducerTighteningResult lastesTightening = await kdu.GetResultAsync(CancellationToken.None);
Console.WriteLine($"{lastesTightening.GetResultTimestamp()} - The torque was {lastesTightening.GetTorqueResult()} cNm and the angle was {lastesTightening.GetAngleResult()} degrees");
```
### Print a tightening result without using async
```C#
// print a tightening result without using async/await
while (true)
{
    Thread.Sleep(1000); // some work being done by your app

    if (kdu.HasNewResult())
    {
        KducerTighteningResult latestResult = kdu.GetResult();
        Console.WriteLine($"{latestResult.GetResultTimestamp()} - The torque was {latestResult.GetTorqueResult()} cNm and the angle was {latestResult.GetAngleResult()} degrees");
        break;
    }
}
```
### Select a program on the K-Ducer controller
```C#
// select a program on the KDU controller
try
{
    await kdu.SelectProgramNumberAsync(60); // select programn number 60
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
```
### Automatically disable (lock) the screwdriver after receiving a result
```C#
// LockScrewdriverIndefinitelyAfterResult
kdu.LockScrewdriverIndefinitelyAfterResult(true); // with this option, kdu will automatically disable the screwdriver after a new result is detected
KducerTighteningResult result = await kdu.GetResultAsync(CancellationToken.None);
// now the screwdriver is disabled and the operator cannot use it
// use EnableScrewdriver() to re-enable it:
await kdu.EnableScrewdriver();
```
## Roadmap
Future features:
- support for changing tightening program parameters
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
