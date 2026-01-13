using UnityEngine;
using TMPro;
using System.Collections;

public class GameUIManager : MonoBehaviour
{
    public static GameUIManager Instance;

    [Header("Top Info Panel")]
    public TextMeshProUGUI textTurnInfo;
    public TextMeshProUGUI textTimer;
    public TextMeshProUGUI textRoundInfo;
    public TextMeshProUGUI txtVaccineStats;
    public TextMeshProUGUI textPhase;

    [Header("My Team Info")]
    public TextMeshProUGUI txtMyTeam;

    [Header("Team Status")]
    public TextMeshProUGUI textTeamA;
    public TextMeshProUGUI textTeamB;

    [Header("My Status")]
    public TextMeshProUGUI textMoney;
    public TextMeshProUGUI textActionPoints;

    public TextMeshProUGUI txtNews;
    public float newsDuration = 4.0f;
    Coroutine newsCo;

    private int deathsA = 0;
    private int deathsB = 0;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        UpdateMoney(0);
        UpdateTimer(0);
        CalculateAndRefreshScore();
        UpdatePhaseText("게임 대기 중...");
    }

    public void UpdateMyTeamText(int team)
    {
        if (txtMyTeam == null) return;

        if (team == 1)
        {
            txtMyTeam.text = "TEAM 1 (RED)";
            txtMyTeam.color = Color.red;
        }
        else if (team == 2)
        {
            txtMyTeam.text = "TEAM 2 (BLUE)";
            txtMyTeam.color = Color.blue;
        }
        else
        {
            txtMyTeam.text = "TEAM ?";
            txtMyTeam.color = Color.white;
        }
    }

    public void UpdatePhaseText(string status)
    {
        if (textPhase != null)
            textPhase.text = status;
    }

    public void UpdateVaccineStats(float recovery, float supply, int range, int used, int max)
    {
        if (txtVaccineStats == null) return;

        txtVaccineStats.text =
            "[백신]\n" +
            $"성공률 {recovery:0}%\n" +
            $"보급률 {supply:0}%\n" +
            $"거리 {range}\n" +
            $"사용: {used}/{max}";
    }

    public void UpdateTimer(float timeRemaining)
    {
        if (timeRemaining < 0) timeRemaining = 0;
        int minutes = Mathf.FloorToInt(timeRemaining / 60);
        int seconds = Mathf.FloorToInt(timeRemaining % 60);

        if (textTimer != null)
            textTimer.text = string.Format("{0:00}:{1:00}", minutes, seconds);

        if (timeRemaining <= 10f) textTimer.color = Color.red;
        else textTimer.color = Color.white;
    }

    public void UpdateTurnInfo(int current, int max)
    {
        if (textTurnInfo != null)
            textTurnInfo.text = "턴 " + current + " / " + max;
    }

    public void UpdateRoundInfo(int currentRound, int maxRounds, int currentTurn, int turnsPerRound)
    {
        int turnInRound = ((currentTurn - 1) % turnsPerRound) + 1;

        string roundTurnText = $"라운드 {currentRound}/{maxRounds} - 턴 {turnInRound}/{turnsPerRound}";
        if (textRoundInfo != null)
            textRoundInfo.text = roundTurnText;

        if (textTurnInfo != null && textRoundInfo == null)
            textTurnInfo.text = roundTurnText;
    }

    public void SetDeathTotals(int team1Deaths, int team2Deaths)
    {
        deathsA = team1Deaths;
        deathsB = team2Deaths;
        CalculateAndRefreshScore();
    }
    public void UpdateScore(int team, int scoreChange, int deathChange)
    {
        if (team == 1) deathsA += deathChange;
        if (team == 2) deathsB += deathChange;
        CalculateAndRefreshScore();
    }

    public void ShowNews(string msg)
    {
        if (txtNews == null) return;

        RectTransform rect = txtNews.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        txtNews.alignment = TextAlignmentOptions.Center;

        if (newsCo != null) StopCoroutine(newsCo);
        newsCo = StartCoroutine(CoShowNews(msg));
    }

    IEnumerator CoShowNews(string msg)
    {
        txtNews.gameObject.SetActive(true);
        txtNews.text = msg;
        yield return new WaitForSeconds(newsDuration);
        txtNews.gameObject.SetActive(false);
    }

    void CalculateAndRefreshScore()
    {
        int countA = 0;
        int countB = 0;

        if (MapGenerator.Instance != null && MapGenerator.Instance.allTiles != null)
        {
            foreach (var tile in MapGenerator.Instance.allTiles.Values)
            {
                if (tile.ownerTeam == 1) countA++;
                else if (tile.ownerTeam == 2) countB++;
            }
        }

        if (textTeamA != null)
            textTeamA.text = $"TEAM A (RED)\n타일: {countA}\n사망: {deathsA}";

        if (textTeamB != null)
            textTeamB.text = $"TEAM B (BLUE)\n타일: {countB}\n사망: {deathsB}";
    }

    public void UpdateMoney(int money)
    {
        if (textMoney != null)
            textMoney.text = $"돈: {money}";
    }

    public void UpdateActionPoints(int current, int max)
    {
        if (textActionPoints != null)
            textActionPoints.text = $"행동: {current}/{max}";
    }

    public void RefreshPlayerStatsUI()
    {
        if (GamePlayer.LocalPlayer == null) return;

        UpdateMoney(GamePlayer.LocalPlayer.money);
        UpdateActionPoints(GamePlayer.LocalPlayer.currentActionPoints, GamePlayer.LocalPlayer.maxActionPoints);
        UpdateVaccineStats(
            GamePlayer.LocalPlayer.vaccineRecoveryRate,
            GamePlayer.LocalPlayer.vaccineSupplyRate,
            GamePlayer.LocalPlayer.vaccineRange,
            GamePlayer.LocalPlayer.currentVaccineUses,
            GamePlayer.LocalPlayer.maxVaccineUses
        );
    }
}
