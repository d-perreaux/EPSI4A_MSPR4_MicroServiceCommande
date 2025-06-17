namespace APICommande.Tests;

public class MathTest
{
    [Fact]
    public void Addition()
    {
        int a = 2, b = 3;
        int resultat = a + b;
        Assert.Equal(5, resultat);
    }
}