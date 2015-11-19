namespace Cricket.Tests

open System
open NUnit.Framework
open Cricket
open Cricket.Math.Statistics

[<TestFixture; Category("Unit")>]
type ``Given a set of data I can incremetally compute``() = 
    
    let result = 
        [1L;2L;-2L;4L;-3L] |> Sampling.compute

    [<Test>]
    member t.``the max of the data``() = 
        Assert.That(result.Max, Is.EqualTo(4).Within(0.0001))

    [<Test>]
    member t.``the min of the data``() = 
        Assert.That(result.Min, Is.EqualTo(-3).Within(0.0001))
        
    [<Test>]
    member t.``the sum of the data``() = 
        Assert.That(result.Sum, Is.EqualTo(2).Within(0.0001))

    [<Test>]
    member t.``the sample size of the data``() = 
        Assert.That(result.Count, Is.EqualTo(5).Within(0.0001)) 
        
    [<Test>]
    member t.``the mean of the data``() = 
        Assert.That(result.Mean, Is.EqualTo(0.4).Within(0.0001))

    [<Test>]
    member t.``the variance of the data``() = 
        Assert.That(result.Variance, Is.EqualTo(8.3).Within(0.0001))
        
    [<Test>]
    member t.``the standard deviation of the data``() = 
        Assert.That(result.StandardDeviation, Is.EqualTo(2.8809).Within(0.0001))
            
    [<Test>]
    member t.``the skewness of the data``() = 
        Assert.That(result.Skewness, Is.EqualTo(-0.02525).Within(0.0001))
        
    [<Test>]
    member t.``the kurtosis of the data``() = 
        Assert.That(result.Kurtosis, Is.EqualTo(1.5489).Within(0.0001))
        
    [<Test>]
    member t.``the excess kurtosis of the data``() =
        Assert.That(result.ExcessKurtosis, Is.EqualTo(-1.4511).Within(0.0001))               
