import requests

for word in ["喂", "哟", "呦", "咦", "诶"]:
    r = requests.post("http://127.0.0.1:5001/translate", json={"text": word})
    print(word, "->", r.json())