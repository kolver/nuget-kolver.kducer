# nuget-kolver.kducer
This library allows you to interface with a Kolver K-Ducer torque driving system without having to worry about the underlying Modbus TCP protocol.
You can obtain tightening results, enable/disable the screwdriver, and run the screwdriver remotely (read the manual and always follow all safety precautions before running the screwdriver).
The library uses TAP (async/await) to implement the underlying cyclical Modbus TCP communications, and uses its own minimal Modbus TCP client.
Brought to you by Kolver www.kolver.com
## Example
code example here
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
