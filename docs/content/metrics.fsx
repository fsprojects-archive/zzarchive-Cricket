(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use 
// it to define helpers that you do not want to show in the documentation.
#I "../../bin"
#r "FSharp.Actor.dll"
open FSharp.Actor
open System.Threading

(**

Metrics
=======

*)

ActorHost.Start(fun c -> 
    { c with 
        Metrics = Some {
            Metrics.Configuration.Default with
                ReportCreator = Metrics.WriteToFile(5000, @"C:\temp\Metrics.txt", Metrics.Formatters.toString)
        }
    })

type Say =
    | Hello
    | HelloWorld
    | Name of string

let greeter = 
    actor {
        name "greeter"
        messageHandler (fun actor ->
            let rec loop() = async {
                let! msg = actor.Receive() //Wait for a message
//                match msg.Message with
//                | Hello ->  actor.Logger.Debug("Hello") //Handle Hello leg
//                | HelloWorld -> actor.Logger.Debug("Hello World") //Handle HelloWorld leg
//                | Name name -> actor.Logger.Debug(sprintf "Hello, %s" name) //Handle Name leg
                return! loop() //Recursively loop

            }
            loop())
    } |> Actor.spawn

let cts = new CancellationTokenSource()

let rec publisher() = async {
    do greeter <-- Name "Metrics"
    return! publisher()
}

Async.Start(publisher(), cts.Token)
cts.Cancel()

greeter <-- Shutdown

(**
Sample metrics output

[{
		"Key" : "system",
		"Values" : []
	}, {
		"Key" : "ping/system_mailbox",
		"Values" : [{
				"Key" : "total_queue_length",
				"Properties" : [{
						"Name" : "Value",
						"Value" : 0
					}
				]
			}, {
				"Key" : "msg_enqueue",
				"Properties" : [{
						"Name" : "One Minute Average",
						"Value" : 0.0000
					}, {
						"Name" : "Five Minute Average",
						"Value" : 0.0000
					}, {
						"Name" : "Fifteen Minute Average",
						"Value" : 0.0000
					}, {
						"Name" : "Mean",
						"Value" : 0.0000
					}, {
						"Name" : "Count",
						"Value" : 0
					}
				]
			}, {
				"Key" : "msg_dequeue",
				"Properties" : [{
						"Name" : "One Minute Average",
						"Value" : 0.0000
					}, {
						"Name" : "Five Minute Average",
						"Value" : 0.0000
					}, {
						"Name" : "Fifteen Minute Average",
						"Value" : 0.0000
					}, {
						"Name" : "Mean",
						"Value" : 0.0000
					}, {
						"Name" : "Count",
						"Value" : 0
					}
				]
			}, {
				"Key" : "inbox_queue_length",
				"Properties" : [{
						"Name" : "Value",
						"Value" : 0
					}
				]
			}, {
				"Key" : "arrivals_queue_length",
				"Properties" : [{
						"Name" : "Value",
						"Value" : 0
					}
				]
			}
		]
	}, {
		"Key" : "ping",
		"Values" : [{
				"Key" : "uptime",
				"Properties" : [{
						"Name" : "Time",
						"Value" : 26357.6355
					}
				]
			}, {
				"Key" : "shutdownCount",
				"Properties" : [{
						"Name" : "Value",
						"Value" : 0
					}
				]
			}, {
				"Key" : "errorCount",
				"Properties" : [{
						"Name" : "Value",
						"Value" : 0
					}
				]
			}, {
				"Key" : "restartCount",
				"Properties" : [{
						"Name" : "Value",
						"Value" : 0
					}
				]
			}
		]
	}, {
		"Key" : "eventstream/host/mailbox",
		"Values" : []
	}, {
		"Key" : "transports/TcpActorRegistryTransport",
		"Values" : [{
				"Key" : "msgs_published",
				"Properties" : [{
						"Name" : "One Minute Average",
						"Value" : 0.0000
					}, {
						"Name" : "Five Minute Average",
						"Value" : 0.0000
					}, {
						"Name" : "Fifteen Minute Average",
						"Value" : 0.0000
					}, {
						"Name" : "Mean",
						"Value" : Infinity
					}, {
						"Name" : "Count",
						"Value" : 1
					}
				]
			}, {
				"Key" : "msgs_received",
				"Properties" : [{
						"Name" : "One Minute Average",
						"Value" : 0.0000
					}, {
						"Name" : "Five Minute Average",
						"Value" : 0.0000
					}, {
						"Name" : "Fifteen Minute Average",
						"Value" : 0.0000
					}, {
						"Name" : "Mean",
						"Value" : Infinity
					}, {
						"Name" : "Count",
						"Value" : 1
					}
				]
			}, {
				"Key" : "time_to_send",
				"Properties" : [{
						"Name" : "Min",
						"Value" : 104
					}, {
						"Name" : "Max",
						"Value" : 104
					}, {
						"Name" : "Average",
						"Value" : 104.0000
					}, {
						"Name" : "Standard Deviation",
						"Value" : NaN
					}
				]
			}
		]
	}, {
		"Key" : "transports/actor.tcp",
		"Values" : [{
				"Key" : "msgs_published",
				"Properties" : [{
						"Name" : "One Minute Average",
						"Value" : 4429.0115
					}, {
						"Name" : "Five Minute Average",
						"Value" : 996.1047
					}, {
						"Name" : "Fifteen Minute Average",
						"Value" : 996.1047
					}, {
						"Name" : "Mean",
						"Value" : 100001.0000
					}, {
						"Name" : "Count",
						"Value" : 100001
					}
				]
			}, {
				"Key" : "msgs_received",
				"Properties" : [{
						"Name" : "One Minute Average",
						"Value" : 4428.9542
					}, {
						"Name" : "Five Minute Average",
						"Value" : 996.0893
					}, {
						"Name" : "Fifteen Minute Average",
						"Value" : 996.0893
					}, {
						"Name" : "Mean",
						"Value" : 100000.0000
					}, {
						"Name" : "Count",
						"Value" : 100000
					}
				]
			}, {
				"Key" : "time_to_send",
				"Properties" : [{
						"Name" : "Min",
						"Value" : 0
					}, {
						"Name" : "Max",
						"Value" : 21
					}, {
						"Name" : "Average",
						"Value" : 0.0032
					}, {
						"Name" : "Standard Deviation",
						"Value" : 0.1170
					}
				]
			}
		]
	}, {
		"Key" : "eventstream/host",
		"Values" : [{
				"Key" : "total_queue_length",
				"Properties" : [{
						"Name" : "Value",
						"Value" : 0
					}
				]
			}, {
				"Key" : "msg_enqueue",
				"Properties" : [{
						"Name" : "One Minute Average",
						"Value" : 0.0000
					}, {
						"Name" : "Five Minute Average",
						"Value" : 0.0000
					}, {
						"Name" : "Fifteen Minute Average",
						"Value" : 0.0000
					}, {
						"Name" : "Mean",
						"Value" : 0.5000
					}, {
						"Name" : "Count",
						"Value" : 1
					}
				]
			}, {
				"Key" : "msg_dequeue",
				"Properties" : [{
						"Name" : "One Minute Average",
						"Value" : 0.0000
					}, {
						"Name" : "Five Minute Average",
						"Value" : 0.0000
					}, {
						"Name" : "Fifteen Minute Average",
						"Value" : 0.0000
					}, {
						"Name" : "Mean",
						"Value" : 0.5000
					}, {
						"Name" : "Count",
						"Value" : 1
					}
				]
			}, {
				"Key" : "inbox_queue_length",
				"Properties" : [{
						"Name" : "Value",
						"Value" : 0
					}
				]
			}, {
				"Key" : "arrivals_queue_length",
				"Properties" : [{
						"Name" : "Value",
						"Value" : 0
					}
				]
			}, {
				"Key" : "subscribers",
				"Properties" : [{
						"Name" : "Value",
						"Value" : 1
					}
				]
			}
		]
	}, {
		"Key" : "ping/mailbox",
		"Values" : [{
				"Key" : "total_queue_length",
				"Properties" : [{
						"Name" : "Value",
						"Value" : 0
					}
				]
			}, {
				"Key" : "msg_enqueue",
				"Properties" : [{
						"Name" : "One Minute Average",
						"Value" : 6994.8395
					}, {
						"Name" : "Five Minute Average",
						"Value" : 4589.0860
					}, {
						"Name" : "Fifteen Minute Average",
						"Value" : 4589.0860
					}, {
						"Name" : "Mean",
						"Value" : 3846.1923
					}, {
						"Name" : "Count",
						"Value" : 100001
					}
				]
			}, {
				"Key" : "msg_dequeue",
				"Properties" : [{
						"Name" : "One Minute Average",
						"Value" : 6994.8395
					}, {
						"Name" : "Five Minute Average",
						"Value" : 4589.0860
					}, {
						"Name" : "Fifteen Minute Average",
						"Value" : 4589.0860
					}, {
						"Name" : "Mean",
						"Value" : 3846.1923
					}, {
						"Name" : "Count",
						"Value" : 100001
					}
				]
			}, {
				"Key" : "inbox_queue_length",
				"Properties" : [{
						"Name" : "Value",
						"Value" : 0
					}
				]
			}, {
				"Key" : "arrivals_queue_length",
				"Properties" : [{
						"Name" : "Value",
						"Value" : 0
					}
				]
			}
		]
	}, {
		"Key" : "transports/UdpRegistryDiscovery",
		"Values" : [{
				"Key" : "msgs_received",
				"Properties" : [{
						"Name" : "One Minute Average",
						"Value" : 13.5536
					}, {
						"Name" : "Five Minute Average",
						"Value" : 4.8726
					}, {
						"Name" : "Fifteen Minute Average",
						"Value" : 4.8726
					}, {
						"Name" : "Mean",
						"Value" : Infinity
					}, {
						"Name" : "Count",
						"Value" : 119
					}
				]
			}
		]
	}, {
		"Key" : "PingNode:10244@HP20024950",
		"Values" : [{
				"Key" : "uptime",
				"Properties" : [{
						"Name" : "Time",
						"Value" : 121682.1670
					}
				]
			}, {
				"Key" : "actorResolutionRequests",
				"Properties" : [{
						"Name" : "Value",
						"Value" : 100001
					}
				]
			}, {
				"Key" : "registeredActors",
				"Properties" : [{
						"Name" : "Value",
						"Value" : 1
					}
				]
			}
		]
	}
]

*)