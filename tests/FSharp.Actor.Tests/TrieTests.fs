namespace FSharp.Actor.Tests

open NUnit.Framework
open FsUnit
open FSharp.Actor

[<TestFixture; Category("Unit")>]
type ``Given a trie``() = 
    
    [<Test>]
    member t.``I can create an empty one``() =
        Trie.empty |> should equal (Trie.Node(None, Map.empty))

    [<Test>]
    member t.``I can add a value to an empty trie``() =
        let actual = 
            Trie.empty |> Trie.add ["a";"b"] "ab"
        let expected = 
            Trie.Node (None,
                        Map [
                            ("a", Trie.Node (None, 
                                                Map [
                                                        ("b", Trie.Node (Some "ab", Map []))
                                                    ]
                                            )
                            )]
                      )
        actual |> should equal expected

    [<Test>]
    member t.``I can add different values with the same root``() =
        let actual = 
            Trie.empty |> Trie.add ["a";"b"] "ab" |> Trie.add ["a"; "c"] "ac"
        let expected = 
            Trie.Node (None,
                        Map [
                            ("a", Trie.Node (None, 
                                                Map [
                                                        ("b", Trie.Node (Some "ab", Map []))
                                                        ("c", Trie.Node (Some "ac", Map []))
                                                    ]
                                            )
                            )]
                      )
        actual |> should equal expected

    [<Test>]
    member t.``I can retrieve a node with its children``() =
        let actual = 
            Trie.add ["a";"b"] "ab" Trie.empty
            |> Trie.add ["a"; "c"] "ac"
            |> Trie.subtrie ["a"]
            |> Trie.values
        printfn "%A" actual
        let expected = ["ab"; "ac"]
        actual |> should equal expected

    [<Test>]
    member t.``I can retrieve a leaf node it should have just its value``() =
        let actual = 
            Trie.add ["a";"b"] "ab" Trie.empty
            |> Trie.add ["a"; "c"] "ac"
            |> Trie.subtrie ["a";"c"]
            |> Trie.values
        printfn "%A" actual
        let expected = ["ac"]
        actual |> should equal expected
        
    [<Test>]
    member t.``I can remove a value from a trie``() =
        let actual = 
            Trie.empty |> Trie.add ["a";"b"] "ab" |> Trie.remove ["a";"b"]
        let expected : Trie.trie<string, string> = Trie.empty
        actual |> should equal expected  
       