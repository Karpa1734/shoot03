using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/*
 * 空のGameObjectにこのスクリプトをアタッチし、
 * ロード画面用SceneをBuild Settingsに登録してそのSceneの名前を変数LoadSceneNameに入力してください。
 * SceneChanger.LoadScene()を呼ぶと自動でロード用Sceneを挟んでロードを行います。
 * トランジションを行う場合は、WaitTimeFromPastScene変数とWaitTimeToNextScene変数を設定してください。
 * ロード中はSceneChanger.Progressからfloat型でロードの進捗を取得できます。(0f ～ 1f)
 * なお、Unityの仕様上、描画順はSceneに依存しないため、
 * ロード画面用Sceneのオブジェクトのレイヤが上位に来るように設定してください。
*/

public class SceneChanger: MonoBehaviour
{
    static private SceneChanger instance = null;
    public AsyncOperation LoadingOperation = null;
    public AsyncOperation UnloadingOperation = null;
    public string LoadSceneName;
    public Scene PastScene;
    [NonSerialized]
    public string NextScene;
    public float WaitTimeFromPastScene;
    public float WaitTimeToNextScene;
    private static SceneChanger Singleton
    {
        get
        {
            if (instance == null) throw new NullReferenceException("SceneChanger does not exist!");
            return instance;
        }
        set { instance = value;}
    }

    public static float Progress
    {
        get
        {
            float progress = 0f;
            if (Singleton.UnloadingOperation != null)
                progress += Singleton.UnloadingOperation.progress * 0.5f;
            if (Singleton.LoadingOperation != null)
                progress += Singleton.LoadingOperation.progress * 0.5f;
            Debug.Log(progress);
            return progress;
        }
    }

    private void Awake()
    {
        if (instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Singleton = this;
        DontDestroyOnLoad(gameObject);
    }

    public static void LoadScene(string nextscenename)
    {
        Singleton.NextScene = nextscenename;
        SceneManager.sceneLoaded += OnLoadScene;
        SceneManager.LoadSceneAsync(Singleton.LoadSceneName, LoadSceneMode.Additive);
    }

    static public void OnLoadScene(Scene scene, LoadSceneMode mode)
    {
        Singleton.PastScene = SceneManager.GetActiveScene();
        SceneManager.sceneLoaded -= OnLoadScene;
        SceneManager.sceneUnloaded += SceneChanger.OnPastSceneUnloaded;
        SceneManager.SetActiveScene(scene);
        Singleton.Invoke("Unload", Singleton.WaitTimeFromPastScene);
    }

    public void Unload()
    {
        Singleton.UnloadingOperation = SceneManager.UnloadSceneAsync(Singleton.PastScene);
    }

    static public void OnPastSceneUnloaded(Scene scene)
    {
        SceneManager.sceneUnloaded -= SceneChanger.OnPastSceneUnloaded;
        SceneManager.sceneLoaded += SceneChanger.OnNextSceneLoaded;
        Singleton.LoadingOperation = SceneManager.LoadSceneAsync(Singleton.NextScene, LoadSceneMode.Additive);
    }

    static public void OnNextSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= SceneChanger.OnNextSceneLoaded;
        SceneManager.SetActiveScene(scene);
        Singleton.Invoke("ToNextScene", Singleton.WaitTimeToNextScene);
    }

    public void ToNextScene()
    {
        foreach(var obj in SceneManager.GetSceneByName(Singleton.LoadSceneName).GetRootGameObjects())
            obj.SetActive(false);
        SceneManager.UnloadSceneAsync(Singleton.LoadSceneName);
        Singleton.UnloadingOperation = null;
        Singleton.LoadingOperation = null;
    }


}
