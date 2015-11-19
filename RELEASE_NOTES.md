#### 0.0.1-alpha - May 22 2013
* First alpha version

#### 0.0.2-alpha - June 14 2013
* Re-factoring and clean-up

#### 0.0.3-alpha - July 27th 2014
* Major changes basically a complete re-write. See docs for new API.

#### 0.0.4-alpha - August 8th 2014
* Changed implementation of TCP transport to use pooled SocketAsyncEventArgs.
* Added fully working remoting example.

#### 0.0.5-alpha - August 20th 2014
* Fixed a overlapped buffer issue on send in TCP transport

#### 0.0.6-alpha - August 27th 2014
* Added some more UDP config options
* Fixed NuGet packaging.
* Merged pull request from @endeavour - adding TryScan and TryReceive functions.

#### 0.0.7-alpha - September 4th 2014
* Fixed some issues with event streams not getting passed when an actor is registered.
* Changed how actor paths are built.
* Cleaned up APIs' in various places specifically around ActorHost configuration.

#### 0.0.8-alpha - November 3rd 2014
* Added metrics and metric reporting capabilities
* Added message tracing support.
* Removed actor System abstraction
* DefaultMailbox is now capped by default at 1000000 items. This is configurable
* Re-arranged solution so it is a far more logical order
* Added supervisors.
* Added simple routing primitive - more to come, shortestQueue etc. 
* Fixed UDP malformed packet and multicast constrianed to single router hop problem (#28)
* Ignored failing tests on build server for now. They pass consistently when run locally.

#### 0.0.9-alpha - November 12th 2014
* Serailizer options now removed from Actor Host and placed in remoting
* Updated documentation

#### 0.0.10-alpha - November 15th 2014
* Added actor lifecycle events - pre/post (Startup, Restart and Shutdown)
* Fixed failing tests on mono and windows

### 0.0.11-alpha - November 19th 2015
* Fixed reference documentation
* Removed half baked remoting solution
