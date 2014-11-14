namespace Cricket.Tests

open System
open NUnit.Framework
open FsUnit
open Cricket
open Cricket.Math.Statistics

[<TestFixture; Category("Unit")>]
type ``Given a set of data I can incremetally compute``() = 
    
    let result = 
        [1L;2L;-2L;4L;-3L] |> Sampling.compute

    [<Test>]
    member t.``the max of the data``() = 
        result.Max |> should be (equalWithin 0.0001 4) 

    [<Test>]
    member t.``the min of the data``() = 
        result.Min |> should be (equalWithin 0.0001 -3)
        
    [<Test>]
    member t.``the sum of the data``() = 
        result.Sum |> should be (equalWithin 0.0001 2)

    [<Test>]
    member t.``the sample size of the data``() = 
        result.Count |> should be (equalWithin 0.0001 5)  
        
    [<Test>]
    member t.``the mean of the data``() = 
        result.Mean |> should be (equalWithin 0.0001 0.4)

    [<Test>]
    member t.``the variance of the data``() = 
        result.Variance |> should be (equalWithin 0.0001 8.3) 
        
    [<Test>]
    member t.``the standard deviation of the data``() = 
        result.StandardDeviation |> should be (equalWithin 0.0001 2.8809)
        
    [<Test>]
    member t.``the skewness of the data``() = 
        result.Skewness |> should be (equalWithin 0.0001 -0.02525)
        
    [<Test>]
    member t.``the kurtosis of the data``() = 
        result.Kurtosis |> should be (equalWithin 0.0001 1.5489)
        
    [<Test>]
    member t.``the excess kurtosis of the data``() = 
        result.ExcessKurtosis |> should be (equalWithin 0.0001 -1.4511)               
