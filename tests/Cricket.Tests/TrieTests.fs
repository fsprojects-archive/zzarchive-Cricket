namespace Cricket.Tests

open NUnit.Framework
open Cricket

[<TestFixture; Category("Unit")>]
type ``Given a trie``() = 
    
    [<Test>]
    member t.``I can create an empty one``() =
        Assert.AreEqual(Trie.Node(None, Map.empty), Trie.empty)

    [<Test>]
    member t.``I can add a value to an empty trie``() =
        let actual = 
            Trie.empty |> Trie.add [TrieKey.Key "a"; TrieKey.Key "b"] "ab"
        let expected = 
            Trie.Node (None,
                        Map [
                            (TrieKey.Key "a", Trie.Node (None, 
                                                Map [
                                                        (TrieKey.Key  "b", Trie.Node (Some "ab", Map []))
                                                    ]
                                            )
                            )]
                      )
        Assert.AreEqual(expected, actual)

    [<Test>]
    member t.``I can add different values with the same root``() =
        let actual = 
            Trie.empty 
            |> Trie.add [TrieKey.Key "a";TrieKey.Key "b"] "ab" 
            |> Trie.add [TrieKey.Key "a";TrieKey.Key "c"] "ac"
        let expected = 
            Trie.Node (None,
                        Map [
                            (TrieKey.Key "a", Trie.Node (None, 
                                                Map [
                                                        (TrieKey.Key "b", Trie.Node (Some "ab", Map []))
                                                        (TrieKey.Key "c", Trie.Node (Some "ac", Map []))
                                                    ]
                                            )
                            )]
                      )
        Assert.AreEqual(expected, actual)

    [<Test>]
    member t.``I can add different values with a different root``() =
        let actual = 
            Trie.empty 
            |> Trie.add [TrieKey.Key "a";TrieKey.Key "b"] "ab" 
            |> Trie.add [TrieKey.Key "b";TrieKey.Key "c"] "bc"
        let expected = 
            Trie.Node (None,
                        Map [
                            (TrieKey.Key "a", Trie.Node (None, 
                                                Map [
                                                        (TrieKey.Key "b", Trie.Node (Some "ab", Map []))
                                                    ]
                                            )
                            )
                            (TrieKey.Key "b", Trie.Node (None, 
                                                Map [
                                                        (TrieKey.Key "c", Trie.Node (Some "bc", Map []))
                                                    ]
                                            )
                            )]
                      )
        Assert.AreEqual(expected, actual)

    [<Test>]
    member t.``I can retrieve a node with its children``() =
        let actual = 
            Trie.add [TrieKey.Key "a";TrieKey.Key "b"] "ab" Trie.empty
            |> Trie.add [TrieKey.Key "a"; TrieKey.Key "c"] "ac"
            |> Trie.subtrie [TrieKey.Key "a"]
            |> Trie.values
        printfn "%A" actual
        let expected = ["ab"; "ac"]
        Assert.AreEqual(expected, actual)

    [<Test>]
    member t.``I can retrieve from a trie with mutiple roots``() =
        let actual = 
            Trie.add [TrieKey.Key "a";TrieKey.Key "b"] "ab" Trie.empty
            |> Trie.add [TrieKey.Key "b"; TrieKey.Key "c"] "bc"
            |> Trie.subtrie [TrieKey.Key "a"]
            |> Trie.values
        printfn "%A" actual
        let expected = ["ab"]
        Assert.AreEqual(expected, actual)

    [<Test>]
    member t.``I can retrieve from a trie with mutiple roots and get the other key``() =
        let actual = 
            Trie.add [TrieKey.Key "a";TrieKey.Key "b"] "ab" Trie.empty
            |> Trie.add [TrieKey.Key "b"; TrieKey.Key "c"] "bc"
            |> Trie.subtrie [TrieKey.Key "b"]
            |> Trie.values
        printfn "%A" actual
        let expected = ["bc"]
        Assert.AreEqual(expected, actual)

    [<Test>]
    member t.``I can retrieve a leaf node it should have just its value``() =
        let actual = 
            Trie.add [TrieKey.Key "a";TrieKey.Key "b"] "ab" Trie.empty
            |> Trie.add [TrieKey.Key "a"; TrieKey.Key "c"] "ac"
            |> Trie.subtrie [TrieKey.Key "a";TrieKey.Key "c"]
            |> Trie.values
        printfn "%A" actual
        let expected = ["ac"]
        Assert.AreEqual(expected, actual)
        
    [<Test>]
    member t.``I can remove a value from a trie``() =
        let actual = 
            Trie.empty |> Trie.add [TrieKey.Key "a";TrieKey.Key "b"] "ab" |> Trie.remove [TrieKey.Key "a";TrieKey.Key "b"]
        let expected : Trie<string> = Trie.empty
        Assert.AreEqual(expected, actual)
        
    [<Test>]
    member t.``I can add a wildcard into a trie key``() =
           let actual = 
                Trie.empty 
                |> Trie.add [TrieKey.Key "a";TrieKey.Key "b"] "ab" 
                |> Trie.add [TrieKey.Key "a";TrieKey.Key "c"] "ac"
                |> Trie.add [TrieKey.Key "a";TrieKey.Key "b";TrieKey.Key "d"] "abd"
                |> Trie.add [TrieKey.Key "a";TrieKey.Key "b";TrieKey.Key "e"] "abe"
                |> Trie.add [TrieKey.Key "a";TrieKey.Key "c";TrieKey.Key "e"] "ace"
                |> Trie.resolve [TrieKey.Key "a"; TrieKey.Wildcard; TrieKey.Key "e"]
           let expected = ["abe"; "ace"]
           Assert.AreEqual(expected, actual)
            