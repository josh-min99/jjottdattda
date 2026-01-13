using UnityEngine;

public class VirusCalculator : MonoBehaviour
{
    // 싱글톤 인스턴스: 어디서든 계산 로직을 호출할 수 있게 함
    public static VirusCalculator Instance;

    void Awake() 
    { 
        Instance = this; 
    }

    // 감염 성공 여부를 판정하는 핵심 함수
    // myInfectionRate: 공격자의 전염률(Infection Rate)
    // targetResistance: 방어자의 저항력 (중립은 0, 적군은 50 등)
    public bool TryInfect(float myInfectionRate, float targetResistance = 0f)
    {
        float successChance = 0f;

        // 1. 저항이 없는 경우 (빈 땅, 중립 지역)
        if (targetResistance <= 0)
        {
            // [절대 평가] 내 전염률 수치가 곧 성공 확률이 됨
            successChance = myInfectionRate; 
        }
        // 2. 저항이 있는 경우 (다른 플레이어의 영토)
        else
        {
            // [상대 평가] 공격력과 저항력의 비율로 승률 계산
            // 공식: 내 점수 / (내 점수 + 상대 점수) * 100
            successChance = (myInfectionRate / (myInfectionRate + targetResistance)) * 100f;
        }

        // 0 ~ 100 사이의 난수(주사위) 생성
        // 혹시 모를 System.Random과의 충돌 방지를 위해 UnityEngine.Random 명시
        float dice = UnityEngine.Random.Range(0f, 100f);
        
        // 주사위 값이 승률보다 낮거나 같으면 성공으로 판정
        return dice <= successChance;
    }

    // 감염 성공 시 발생할 사망자 수를 계산하는 함수
    public int CalculateDeaths(int currentPopulation, float fatalityRate)
    {
        // 인구 * (치사율 / 100) 계산 후 반올림하여 정수 반환
        return Mathf.RoundToInt(currentPopulation * (fatalityRate / 100f));
    }
}