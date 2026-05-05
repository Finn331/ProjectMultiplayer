using UnityEngine;

public static class PlayerCurrencyWallet
{
    private const string CurrencyKey = "pm.currency";
    private const string InitKey = "pm.currency.initialized";

    public static int GetBalance()
    {
        return PlayerPrefs.GetInt(CurrencyKey, 0);
    }

    public static void InitializeIfNeeded(int initialAmount)
    {
        if (PlayerPrefs.GetInt(InitKey, 0) == 1)
        {
            return;
        }

        PlayerPrefs.SetInt(CurrencyKey, Mathf.Max(0, initialAmount));
        PlayerPrefs.SetInt(InitKey, 1);
        PlayerPrefs.Save();
    }

    public static void SetBalance(int value)
    {
        PlayerPrefs.SetInt(CurrencyKey, Mathf.Max(0, value));
        PlayerPrefs.Save();
    }

    public static int Add(int value)
    {
        int current = GetBalance();
        int next = Mathf.Max(0, current + value);
        PlayerPrefs.SetInt(CurrencyKey, next);
        PlayerPrefs.Save();
        return next;
    }

    public static bool TrySpend(int value)
    {
        if (value <= 0)
        {
            return true;
        }

        int current = GetBalance();
        if (current < value)
        {
            return false;
        }

        PlayerPrefs.SetInt(CurrencyKey, current - value);
        PlayerPrefs.Save();
        return true;
    }

    public static void ResetWallet(int value = 0)
    {
        PlayerPrefs.SetInt(CurrencyKey, Mathf.Max(0, value));
        PlayerPrefs.SetInt(InitKey, 1);
        PlayerPrefs.Save();
    }
}
