# RotBoiRemastered

How to run:

1). Start setup.bat and wait till install completes
2). Start run.bat


Info on what this is doing - For Tyler:
The setup utilizes virtual environments. It packages all the things you need to run into it's own environment. This prevents your project from being entangled with your own computer's packages and modules.
Any time you need to add a new module to the game or dependency run these commands:

venv/Scripts/activate
pip install [insert package here]
pip freeze > requirement.txt
deactivate