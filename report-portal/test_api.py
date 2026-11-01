import requests

BASE = "https://jsonplaceholder.typicode.com"

def test_get_post():
    r = requests.get(f"{BASE}/posts/1", timeout=10)
    assert r.status_code == 200
    assert r.json()["id"] == 1

def test_list_users():
    r = requests.get(f"{BASE}/users", timeout=10)
    assert r.status_code == 200
    assert len(r.json()) == 10

def test_not_found():
    r = requests.get(f"{BASE}/posts/99999", timeout=10)
    assert r.status_code == 404

def test_intentional_fail():
    assert 1 + 1 == 3